using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StationApi.Services;

namespace StationApi.Controllers;

public class WorkspaceController(FleetApiClient api) : Controller
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor.RouteValues["action"] is not (nameof(Login) or nameof(Register)))
        {
            try
            {
                var user = await api.Send("/api/auth/me"); ViewBag.CurrentUser = user;
                if (user.GetProperty("role").GetString() == "RIDER" && context.ActionDescriptor.RouteValues["action"] is not (nameof(Profile) or nameof(Logout)))
                { context.Result = RedirectToAction("Index", "Rider"); return; }
            }
            catch (ApiFailure ex) when (ex.Status == HttpStatusCode.Unauthorized) { context.Result = RedirectToAction(nameof(Login)); return; }
        }
        var result = await next();
        if (result.Exception is ApiFailure failure)
        {
            result.ExceptionHandled = true;
            Response.StatusCode = (int)failure.Status;
            result.Result = View("Error", failure.Message);
        }
    }
    [HttpGet]
    public IActionResult Login() => View();
    [HttpPost]
    public async Task<IActionResult> Login(string email, string password)
    {
        try { await api.Send("/api/auth/login", HttpMethod.Post, new { email, password }); return RedirectToAction(nameof(Dashboard)); }
        catch (ApiFailure ex) { ViewBag.Error = ex.Message; ViewBag.Email = email; return View(); }
    }
    [HttpGet]
    public IActionResult Register() => View();
    [HttpPost]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        if (!ModelState.IsValid) { request.Password = ""; return View(request); }
        try { await api.Send("/api/auth/register", HttpMethod.Post, request); TempData["Success"] = "Account created. Please sign in."; return RedirectToAction(nameof(Login)); }
        catch (ApiFailure ex) { ModelState.AddModelError("", ex.Message); request.Password = ""; return View(request); }
    }
    [HttpGet]
    public async Task<IActionResult> Metrics() => View(await api.Send("/api/metrics"));
    [HttpGet]
    public async Task<IActionResult> Reports() => View(await api.Send("/api/rider-issues"));
    [HttpPost]
    public async Task<IActionResult> ResolveReport(long id)
    { await api.Send($"/api/rider-issues/{id}/resolve", HttpMethod.Post); return RedirectToAction(nameof(Reports)); }
    [HttpPost]
    public async Task<IActionResult> Logout() { await api.Send("/api/auth/logout", HttpMethod.Post); return RedirectToAction(nameof(Login)); }
    [HttpGet]
    public async Task<IActionResult> Dashboard() => View(await api.Send("/api/dashboard"));
    [HttpGet]
    public async Task<IActionResult> Stations(string? search, string? status)
    {
        var stations = (await api.Send("/api/stations")).EnumerateArray().Where(x =>
            (string.IsNullOrWhiteSpace(search) || x.GetProperty("station").GetProperty("name").GetString()!.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.GetProperty("station").GetProperty("id").GetString()!.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(status) || x.GetProperty("status").GetString() == status)).ToList();
        ViewBag.Search = search; ViewBag.Status = status; return View(stations);
    }
    [HttpGet]
    public async Task<IActionResult> Station(string id) => View(await api.Send($"/api/stations/{Uri.EscapeDataString(id)}"));
    [HttpGet]
    public async Task<IActionResult> Alerts(string? severity, string? status = "ACTIVE", string? station = null)
    {
        ViewBag.Severity = severity; ViewBag.Status = status; ViewBag.Station = station;
        return View(await api.Send($"/api/alerts?{Query("severity", severity)}&{Query("status", status)}&{Query("station", station)}"));
    }
    [HttpGet]
    public async Task<IActionResult> Recommendations(string? status, long? updatedId)
    { ViewBag.UpdatedId = updatedId; ViewBag.Status = status; return View(await api.Send($"/api/recommendations?{Query("status", status)}")); }
    [HttpGet]
    public async Task<IActionResult> Users() => View(await api.Send("/api/users"));
    [HttpGet]
    public async Task<IActionResult> EditUser(int? id)
    {
        if (((JsonElement)ViewBag.CurrentUser).GetProperty("role").GetString() != "ADMIN") return StatusCode(403);
        return View(id.HasValue ? await api.Send($"/api/users/{id}") : default(JsonElement));
    }
    [HttpPost]
    public async Task<IActionResult> SaveUser(int? id, string name, string email, string role, bool enabled, string? password)
    {
        try
        {
            await api.Send(id.HasValue ? $"/api/users/{id}" : "/api/users", id.HasValue ? HttpMethod.Put : HttpMethod.Post,
                new { name, email, role, enabled, password = string.IsNullOrEmpty(password) ? null : password });
            TempData["Success"] = "User saved."; return RedirectToAction(nameof(Users));
        }
        catch (ApiFailure ex)
        {
            ViewBag.Error = ex.Message;
            return View("EditUser", JsonSerializer.SerializeToElement(new { id, name, email, role, enabled }));
        }
    }
    [HttpGet]
    public async Task<IActionResult> Profile() => View(await api.Send("/api/auth/me"));
    [HttpGet]
    public IActionResult Confirm(string kind, long id, string status)
    {
        if (kind is not ("alert" or "recommendation" or "user")) return BadRequest();
        ViewBag.Kind = kind; ViewBag.Id = id; ViewBag.TargetStatus = status; return View();
    }
    [HttpPost]
    public async Task<IActionResult> Apply(string kind, long id, string status)
    {
        var path = kind switch { "alert" => $"/api/alerts/{id}", "recommendation" => $"/api/recommendations/{id}",
            "user" => $"/api/users/{id}/enabled", _ => null };
        if (path is null) return BadRequest();
        var saved = await api.Send(path, HttpMethod.Patch, kind == "user" ? new { enabled = status == "ENABLED" } : (object)new { status });
        TempData["Success"] = kind == "recommendation"
            ? $"Task #{id}: {Models.ViewText.Label(saved.GetProperty("status").GetString()!)}."
            : "Changes saved.";
        if (kind == "alert" && saved.TryGetProperty("stationId", out var stationId)) return RedirectToAction(nameof(Station), new { id = stationId.GetString() });
        if (kind == "recommendation")
        {
            return RedirectToAction(nameof(Recommendations), new { status = saved.GetProperty("status").GetString(), updatedId = id });
        }
        return RedirectToAction(kind switch { "alert" => nameof(Alerts), "user" => nameof(Users), _ => nameof(Recommendations) });
    }
    private static string Query(string key, string? value) => string.IsNullOrWhiteSpace(value) ? "" : $"{key}={Uri.EscapeDataString(value)}";
}
