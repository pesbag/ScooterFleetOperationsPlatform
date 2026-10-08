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
        if (context.ActionDescriptor.RouteValues["action"] != nameof(Login))
        {
            try { ViewBag.CurrentUser = await api.Send("/api/auth/me"); }
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
    public async Task<IActionResult> Recommendations(string? status)
    { ViewBag.Status = status; return View(await api.Send($"/api/recommendations?{Query("status", status)}")); }
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
        await api.Send(path, HttpMethod.Patch, kind == "user" ? new { enabled = status == "ENABLED" } : (object)new { status });
        TempData["Success"] = "Changes saved.";
        return RedirectToAction(kind switch { "alert" => nameof(Alerts), "user" => nameof(Users), _ => nameof(Recommendations) });
    }
    private static string Query(string key, string? value) => string.IsNullOrWhiteSpace(value) ? "" : $"{key}={Uri.EscapeDataString(value)}";
}
