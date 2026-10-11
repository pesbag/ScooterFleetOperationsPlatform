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
        app.MapGet("/__test_rider", async (Microsoft.AspNetCore.Http.HttpContext context) =>
        {
            await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "3"), new Claim(ClaimTypes.Role, "RIDER")], CookieAuthenticationDefaults.AuthenticationScheme)));
            return "test rider";
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
            await client.GetAsync("/__test_rider");
            foreach (var path in new[] { "/api/users", "/api/stations", "/api/dashboard", "/api/Station", "/api/recommendations", "/api/alerts", "/api/metrics", "/api/rider-issues" })
                Assert((await client.GetAsync(path)).StatusCode == HttpStatusCode.Forbidden, $"Rider blocked from operational API: {path}");
            await client.GetAsync("/__test_operator");
            Assert((await client.GetAsync("/api/users")).StatusCode == HttpStatusCode.Forbidden, "Operator blocked from user administration by backend");
            foreach (var path in new[] { "/Workspace/Login", "/", "/Workspace/Stations", "/Workspace/Station?id=S01",
                "/Workspace/Alerts", "/Workspace/Recommendations", "/Workspace/Users", "/Workspace/EditUser",
                "/Workspace/EditUser?id=1", "/Workspace/Profile", "/Workspace/Register", "/Workspace/Metrics", "/Workspace/Reports", "/Rider/Index", "/Rider/Report?id=S01", "/Workspace/Confirm?kind=recommendation&id=1&status=COMPLETED" })
            {
                var response = await client.GetAsync(path);
                var html = await response.Content.ReadAsStringAsync();
                Assert(response.IsSuccessStatusCode, $"Razor View renders: {path} ({(int)response.StatusCode})");
                Assert(!html.Contains("<script", StringComparison.OrdinalIgnoreCase), $"No JavaScript in {path}");
                if (html.Contains("method=\"post\"")) Assert(html.Contains("__RequestVerificationToken"), $"CSRF token included: {path}");
            }
            var stationHtml = await client.GetStringAsync("/Workspace/Stations");
            Assert(stationHtml.Contains("Missing data") && stationHtml.Contains("Outdated data") && stationHtml.Contains("Empty station"), "Razor distinguishes missing, outdated and empty stations");
            Assert(stationHtml.Contains("Unknown") && stationHtml.Contains("Reported:") && stationHtml.Contains("Received:"), "Razor shows unknown availability and separate timestamps");
            var missingDetail = await client.GetStringAsync("/Workspace/Station?id=S04");
            Assert(missingDetail.Contains("Unknown") && missingDetail.Contains("Missing data") && missingDetail.Contains("Not predicted"), "Missing station detail does not show zero or a forecast");
            var automatic = await client.GetStringAsync("/Workspace/Stations");
            Assert(automatic.Contains("http-equiv=\"refresh\"") && automatic.Contains("content=\"60\""), "Read-only station view refreshes automatically without JavaScript");
            var paused = await client.GetStringAsync("/Workspace/Stations?refresh=off");
            Assert(!paused.Contains("http-equiv=\"refresh\""), "Automatic refresh can be paused while searching");
            var registration = await client.GetStringAsync("/Workspace/Register");
            Assert(!registration.Contains("http-equiv=\"refresh\""), "Registration input is not interrupted by automatic refresh");
            var dashboardHtml = await client.GetStringAsync("/");
            Assert(dashboardHtml.Contains("Available bikes at stations") && !dashboardHtml.Contains("Station analysis may be cached"), "Dashboard keeps availability labels without technical paragraphs");
            var confirmation = await client.GetStringAsync("/Workspace/Confirm?kind=alert&id=1&status=RESOLVED");
            var tokenMatch = System.Text.RegularExpressions.Regex.Match(confirmation, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            var fields = new Dictionary<string,string> { ["kind"] = "alert", ["id"] = "1", ["status"] = "RESOLVED", ["__RequestVerificationToken"] = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value) };
            var resolved = await client.PostAsync("/Workspace/Apply", new FormUrlEncodedContent(fields));
            Assert(resolved.StatusCode == HttpStatusCode.Redirect && resolved.Headers.Location!.ToString().Contains("Station"), "Successful resolution redirects to freshly loaded station details");
            fields["id"] = "2";
            var failed = await client.PostAsync("/Workspace/Apply", new FormUrlEncodedContent(fields));
            Assert(failed.StatusCode == HttpStatusCode.Conflict && (await failed.Content.ReadAsStringAsync()).Contains("Saving failed"), "Simulated persistence failure is shown without a success redirect");
            fields["kind"] = "recommendation";
            fields["id"] = "1";
            foreach (var status in new[] { "IN_PROGRESS", "COMPLETED" })
            {
                fields["status"] = status;
                var changed = await client.PostAsync("/Workspace/Apply", new FormUrlEncodedContent(fields));
                var location = changed.Headers.Location?.ToString() ?? "";
                Assert(changed.StatusCode == HttpStatusCode.Redirect && location.Contains("Recommendations") && location.Contains(status),
                    $"Task update returns to its new status filter: {status}");
                var list = await client.GetStringAsync(location);
                Assert(list.Contains($"class=\"task-status\">{StationApi.Models.ViewText.Label(status)}") && list.Contains("updated-task"),
                    $"Updated task is visible with saved status: {status}");
                if (status == "COMPLETED") Assert(!list.Contains("Mark completed"), "Completed task no longer offers completion");
            }
            Assert(dashboardHtml.Contains("account-actions") && dashboardHtml.Contains("/Workspace/Logout"), "Sign out is available above page content");
            var htmlLogin = await (await client.GetAsync("/Workspace/Login")).Content.ReadAsStringAsync();
            Assert(htmlLogin.IndexOf("/Workspace/Register", StringComparison.Ordinal) < htmlLogin.IndexOf("</section>", htmlLogin.IndexOf("login-panel", StringComparison.Ordinal), StringComparison.Ordinal), "Registration link is inside the sign-in panel");
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
        private string taskStatus = "PENDING";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Patch && request.RequestUri!.AbsolutePath == "/api/recommendations/1")
            {
                var payload = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
                taskStatus = payload.GetProperty("status").GetString()!;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 1, status = taskStatus }) };
            }
            var now = DateTime.UtcNow;
            var engine = new SmartFleetEngine(new());
            var stations = Enumerable.Range(1, 12).Select(i => engine.Analyze(new StationInput($"S{i:00}",
                new[] { "Harbor Promenade", "Central Station", "University North", "Market Square", "Riverside Park", "City Hall",
                "Arts District", "East Gardens", "Library Plaza", "West Terminal", "Marina Walk", "Technology Campus" }[i - 1], 40,
                i == 1 ? 4 : i == 2 ? 37 : i == 3 ? 9 : 20, i == 1 ? 36 : i == 2 ? 3 : i == 3 ? 31 : 20,
                now, true, true, 32, 34, [new(now.AddHours(-1), i == 1 ? 10 : i == 2 ? 31 : i == 3 ? 12 : 20),
                new(now, i == 1 ? 4 : i == 2 ? 37 : i == 3 ? 9 : 20)]), now)).ToList();
            stations[3] = engine.Analyze(stations[3].Station with { Count = null }, now);
            stations[4] = engine.Analyze(stations[4].Station with { Count = 0, ReportedAt = now.AddHours(-1), ReceivedAt = now }, now);
            stations[5] = engine.Analyze(stations[5].Station with { Count = 0 }, now);
            var user = new { id = 1, name = "Fleet Administrator", email = "admin@bike.local", role = "ADMIN", enabled = true, createdAt = now.AddDays(-20) };
            var alerts = new[] { new { id = 1, stationId = "S01", severity = "CRITICAL", status = "ACTIVE", title = "Harbor Promenade: availability risk", description = "Expected to run out in approximately 40 minutes at the recent net rate.", createdAt = now } };
            var moves = new[] { new { id = 1, sourceStationId = "S02", destinationStationId = "S01", quantity = 12,
                priority = "CRITICAL", status = taskStatus, reason = "Move 12 bikes from Central Station to Harbor Promenade. Availability is declining at the destination.", createdAt = now } };
            if (request.Method == HttpMethod.Patch && request.RequestUri!.AbsolutePath == "/api/alerts/2")
                return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { message = "Saving failed. Try again." }) };
            object body = request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/me" or "/api/users/1" => user,
                "/api/users" => new[] { user }, "/api/stations" => stations,
                "/api/stations/S01" => new { analysis = stations[0], alerts, recommendations = moves },
                "/api/stations/S04" => new { analysis = stations[3], alerts, recommendations = moves },
                "/api/rider/stations" => new[] { new { id = "S01", name = "Harbor", count = (int?)4, dataState = "CURRENT", reportedAt = now, receivedAt = now, renting = true, favorite = false } },
                "/api/rider/issues" or "/api/rider-issues" => new[] { new { id = 1, stationId = "S01", description = "Broken bike", status = "OPEN", createdAt = now } },
                "/api/metrics" => new { historyMinutes = 120, emptyMinutes = 5, stationsWithHistory = 1, responseMinutes = (double?)null,
                    responseSamples = 0, completionPercent = (double?)null, completed = 0, taskCount = 0, forecastError = (double?)null,
                    forecastSamples = 0, forecastsIssued = 0, destinationChange = (double?)null, changeSamples = 0,
                    stations = new[] { new { name = "Harbor", emptyMinutes = 5, readings = 2 } } },
                "/api/alerts/1" => new { id = 1, stationId = "S01", status = "RESOLVED" },
                "/api/alerts" => alerts, "/api/recommendations" => moves,
                "/api/dashboard" => new { dataMode = "Test fixture", updatedAt = now, healthy = 9, warning = 1, critical = 2,
                    stale = 2, availableBikes = 230, currentStations = 10, totalStations = 12, lastReportedAt = now, lastReceivedAt = now, availableCapacity = 250, activeAlerts = 1, pendingRecommendations = 1,
                    stations, alerts, recommendations = moves },
                _ => new { message = "Fixture route missing" }
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        }
    }
}
