using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using StationApi.Controllers;
using StationApi.Services;

// Isolated HTTP tests render the real Razor Views using an API fixture. This is not an
// end-to-end database test and the fixture is never registered in the production application.
public static class WebChecks
{
    public static async Task Run(bool serve)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName = typeof(WorkspaceController).Assembly.GetName().Name,
            ContentRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../StationApi")) });
        builder.WebHost.UseUrls("http://127.0.0.1:5188");
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
            .AddApplicationPart(typeof(WorkspaceController).Assembly);
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        builder.Services.AddAuthorization(); builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<FleetApiClient>();
        builder.Services.AddHttpClient("FleetApi", c => c.BaseAddress = new Uri("http://fixture.invalid"))
            .ConfigurePrimaryHttpMessageHandler(() => new FixtureHandler());
        var app = builder.Build();
        app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.MapControllers(); app.MapControllerRoute("views", "{controller=Workspace}/{action=Dashboard}/{id?}");
        app.MapGet("/__test_operator", async (Microsoft.AspNetCore.Http.HttpContext context) =>
        {
            await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "2"), new Claim(ClaimTypes.Role, "OPERATOR")], CookieAuthenticationDefaults.AuthenticationScheme)));
            return "test operator";
        });
        await app.StartAsync();
        if (serve) { Console.WriteLine("Razor fixture preview: http://127.0.0.1:5188 (synthetic API fixture, not live storage)"); await app.WaitForShutdownAsync(); return; }
        using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
            { BaseAddress = new Uri("http://127.0.0.1:5188") };
        try
        {
            foreach (var path in new[] { "/api/users", "/api/stations", "/api/alerts", "/api/recommendations", "/api/dashboard", "/api/Station" })
            {
                var response = await client.GetAsync(path);
                Assert(response.StatusCode == HttpStatusCode.Unauthorized, $"Anonymous access blocked: {path}");
            }
            await client.GetAsync("/__test_operator");
            Assert((await client.GetAsync("/api/users")).StatusCode == HttpStatusCode.Forbidden, "Operator blocked from user administration by backend");
            foreach (var path in new[] { "/Workspace/Login", "/", "/Workspace/Stations", "/Workspace/Station?id=S01",
                "/Workspace/Alerts", "/Workspace/Recommendations", "/Workspace/Users", "/Workspace/EditUser",
                "/Workspace/EditUser?id=1", "/Workspace/Profile", "/Workspace/Confirm?kind=recommendation&id=1&status=COMPLETED" })
            {
                var response = await client.GetAsync(path);
                var html = await response.Content.ReadAsStringAsync();
                Assert(response.IsSuccessStatusCode, $"Razor View renders: {path} ({(int)response.StatusCode})");
                Assert(!html.Contains("<script", StringComparison.OrdinalIgnoreCase), $"No JavaScript in {path}");
                if (html.Contains("method=\"post\"")) Assert(html.Contains("__RequestVerificationToken"), $"CSRF token included: {path}");
            }
            var htmlLogin = await (await client.GetAsync("/Workspace/Login")).Content.ReadAsStringAsync();
            Assert(htmlLogin.Contains("type=\"password\""), "Password input is masked");
            Assert((await client.PostAsync("/Workspace/Login", new FormUrlEncodedContent(new Dictionary<string, string> {
                ["email"] = "admin@example.com", ["password"] = "test-password" }))).StatusCode == HttpStatusCode.BadRequest,
                "Form writes without CSRF token rejected");
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }
    private static void Assert(bool condition, string description)
    { if (!condition) throw new Exception($"FAIL: {description}"); Console.WriteLine($"PASS: {description}"); }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var engine = new SmartFleetEngine(new());
            var stations = Enumerable.Range(1, 12).Select(i => engine.Analyze(new StationInput($"S{i:00}",
                new[] { "Harbor Promenade", "Central Station", "University North", "Market Square", "Riverside Park", "City Hall",
                "Arts District", "East Gardens", "Library Plaza", "West Terminal", "Marina Walk", "Technology Campus" }[i - 1], 40,
                i == 1 ? 4 : i == 2 ? 37 : i == 3 ? 9 : 20, i == 1 ? 36 : i == 2 ? 3 : i == 3 ? 31 : 20,
                now, true, true, 32, 34, [new(now.AddHours(-1), i == 1 ? 10 : i == 2 ? 31 : i == 3 ? 12 : 20),
                new(now, i == 1 ? 4 : i == 2 ? 37 : i == 3 ? 9 : 20)]), now)).ToList();
            var user = new { id = 1, name = "Fleet Administrator", email = "admin@scooter.local", role = "ADMIN", enabled = true, createdAt = now.AddDays(-20) };
            var alerts = new[] { new { id = 1, stationId = "S01", severity = "CRITICAL", status = "ACTIVE", title = "Harbor Promenade: availability risk", description = "Expected to run out in approximately 40 minutes at the recent net rate.", createdAt = now } };
            var moves = new[] { new { id = 1, sourceStationId = "S02", destinationStationId = "S01", quantity = 12,
                priority = "CRITICAL", status = "PENDING", reason = "Move 12 scooters from Central Station to Harbor Promenade. Availability is declining at the destination.", createdAt = now } };
            object body = request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/me" or "/api/users/1" => user,
                "/api/users" => new[] { user }, "/api/stations" => stations,
                "/api/stations/S01" => new { analysis = stations[0], alerts, recommendations = moves },
                "/api/alerts" => alerts, "/api/recommendations" => moves,
                "/api/dashboard" => new { dataMode = "Test fixture", updatedAt = now, healthy = 9, warning = 1, critical = 2,
                    stale = 0, totalScooters = 230, availableCapacity = 250, activeAlerts = 1, pendingRecommendations = 1,
                    stations, alerts, recommendations = moves },
                _ => new { message = "Fixture route missing" }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        }
    }
}
