using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StationApi.Services;
namespace StationApi.Controllers;

public class RiderController(FleetApiClient api) : Controller
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try { ViewBag.CurrentUser = await api.Send("/api/auth/me"); }
        catch (ApiFailure ex) when (ex.Status == HttpStatusCode.Unauthorized) { context.Result = RedirectToAction("Login", "Workspace"); return; }
        var result = await next();
        if (result.Exception is ApiFailure failure)
        { result.ExceptionHandled = true; Response.StatusCode = (int)failure.Status; result.Result = View("~/Views/Workspace/Error.cshtml", failure.Message); }
    }
    [HttpGet]
    public async Task<IActionResult> Index(string? search, bool favorites = false)
    {
        var stations = (await api.Send("/api/rider/stations")).EnumerateArray().Where(x =>
            (!favorites || x.GetProperty("favorite").GetBoolean()) && (string.IsNullOrWhiteSpace(search)
            || x.GetProperty("name").GetString()!.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        ViewBag.Search = search; ViewBag.Favorites = favorites; return View(stations);
    }
    [HttpPost]
    public async Task<IActionResult> Favorite(string id, bool remove = false)
    { await api.Send($"/api/rider/favorites/{Uri.EscapeDataString(id)}", remove ? HttpMethod.Delete : HttpMethod.Post); return RedirectToAction(nameof(Index)); }
    [HttpGet]
    public async Task<IActionResult> Report(string id)
    { ViewBag.StationId = id; return View(await api.Send("/api/rider/issues")); }
    [HttpPost]
    public async Task<IActionResult> Report(string stationId, string description)
    {
        try { await api.Send("/api/rider/issues", HttpMethod.Post, new { stationId, description }); TempData["Success"] = "Report sent to the operations team."; return RedirectToAction(nameof(Report), new { id = stationId }); }
        catch (ApiFailure ex) { ViewBag.Error = ex.Message; ViewBag.StationId = stationId; return View(await api.Send("/api/rider/issues")); }
    }
}
