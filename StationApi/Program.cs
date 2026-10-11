using StackExchange.Redis;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using StationApi.DataDbContext;
using StationApi.Models;
using StationApi.Repository;
using StationApi.Services;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["DataSource:StationPrefix"]))
    throw new InvalidOperationException("Set DataSource:StationPrefix to the same value as ingestion.");
if (builder.Configuration.GetValue<int>("Ui:RefreshSeconds") is < 15 or > 600)
    throw new InvalidOperationException("Ui:RefreshSeconds must be between 15 and 600.");
builder.Services.AddDbContext<StationDbContext>(o => o.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"), new MySqlServerVersion(new Version(8, 4, 0))));
builder.Services.AddDbContext<OperationsDbContext>(o => o.UseMySql(builder.Configuration.GetConnectionString("OperationsConnection"), new MySqlServerVersion(new Version(8, 4, 0))));
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(builder.Configuration["MongoDb:ConnectionString"]));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(builder.Configuration["MongoDb:DatabaseName"]));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(
    builder.Configuration["Redis:ConnectionString"] ?? throw new InvalidOperationException("Set Redis:ConnectionString.")));
builder.Services.AddScoped<IStationRepository, StationRepository>();
builder.Services.AddScoped<IPasswordHasher<FleetUser>, PasswordHasher<FleetUser>>();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "BikeFleet.Session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async ctx =>
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<OperationsDbContext>();
        if (!int.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) { ctx.RejectPrincipal(); return; }
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (user is null || !user.Enabled || user.SessionVersion.ToString() != ctx.Principal?.FindFirstValue("version")) ctx.RejectPrincipal();
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("FleetApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5138");
    client.Timeout = TimeSpan.FromSeconds(20);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
builder.Services.AddScoped<FleetApiClient>();
builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
var fleetOptions = builder.Configuration.GetSection("SmartFleet").Get<FleetOptions>() ?? new();
if (!fleetOptions.IsValid()) throw new InvalidOperationException("Invalid SmartFleet thresholds.");
builder.Services.AddSingleton(fleetOptions); builder.Services.AddSingleton<SmartFleetEngine>();
builder.Services.AddSingleton<FleetCacheState>();
builder.Services.AddScoped<StationStateService>();
builder.Services.AddSingleton<OperationsGate>(); builder.Services.AddScoped<FleetDataService>();
builder.Services.AddScoped<MetricsService>();
builder.Services.AddScoped<OperationsService>(); builder.Services.AddHostedService<FleetAnalysisWorker>();
builder.Services.AddDataProtection();
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'none'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; form-action 'self'";
    ctx.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (Exception ex) when (!ctx.Response.HasStarted)
    {
        app.Logger.LogError(ex, "Request failed: {Path}", ctx.Request.Path);
        ctx.Response.StatusCode = ex is DbUpdateException ? 409 : 503;
        if (ctx.Request.Path.StartsWithSegments("/api"))
            await ctx.Response.WriteAsJsonAsync(new { message = ex is DbUpdateException
                ? "The record could not be saved. It may conflict with an existing record."
                : "A required service is temporarily unavailable. Please try again shortly." });
        else { ctx.Response.ContentType = "text/html; charset=utf-8"; await ctx.Response.WriteAsync("<h1>Service temporarily unavailable</h1><p>Please try again shortly.</p><a href='/'>Return to dashboard</a>"); }
    }
});
app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/swagger"), branch => branch.Use(async (ctx, next) =>
    { ctx.Response.Headers.Remove("Content-Security-Policy"); await next(); }));
    app.UseSwagger(); app.UseSwaggerUI();
}
app.MapControllers();
app.MapControllerRoute("views", "{controller=Workspace}/{action=Dashboard}/{id?}");
app.MapGet("/health", async (OperationsDbContext db, IMongoDatabase mongo, CancellationToken token) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", token);
    await mongo.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1), cancellationToken: token);
    return Results.Ok(new { status = "ready" });
});
// The new operational database is separate: never recreate the ingestion database.
for (var attempt = 1; ; attempt++)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await db.Database.EnsureCreatedAsync();
        await OperationsSchema.Upgrade(db);
        if (!await db.Users.AnyAsync())
        {
            var email = builder.Configuration["SeedAdmin:Email"];
            var password = builder.Configuration["SeedAdmin:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || password.Length < 10)
                throw new InvalidOperationException("Set SeedAdmin__Email and SeedAdmin__Password (at least 10 characters) before first startup.");
            var admin = new FleetUser { Email = email.Trim().ToLowerInvariant(), Name = "Fleet Administrator", Role = "ADMIN" };
            admin.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<FleetUser>>().HashPassword(admin, password);
            db.Users.Add(admin); await db.SaveChangesAsync();
        }
        break;
    }
    catch (Exception ex) when (attempt < 20)
    { app.Logger.LogWarning(ex, "Database initialization attempt {Attempt} failed.", attempt); await Task.Delay(3000); }
}
await app.RunAsync();
