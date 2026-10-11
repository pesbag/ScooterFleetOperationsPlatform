using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StationApi.DataDbContext;
using StationApi.Models;
using StationApi.Services;
namespace StationApi.Controllers;

public record RiderIssueRequest([Required, StringLength(255)] string StationId, [Required, StringLength(1000, MinimumLength = 5)] string Description);
[ApiController, Authorize(Roles = "RIDER"), Route("api/rider")]
public class RiderApiController(OperationsDbContext db, FleetDataService data) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("stations")]
    public async Task<IActionResult> Stations(CancellationToken token)
    {
        var favorites = await db.RiderFavorites.Where(x => x.UserId == UserId).Select(x => x.StationId).ToListAsync(token);
        var stations = await data.GetDashboardStations(token);
        return Ok(stations.Select(x => new { id = x.Station.Id, name = x.Station.Name,
            count = x.Stale || x.Station.Renting != true ? null : x.Station.Count, x.DataState,
            x.Station.ReportedAt, x.Station.ReceivedAt, x.Station.Renting, favorite = favorites.Contains(x.Station.Id) }));
    }
    [HttpPost("favorites/{id}")]
    public async Task<IActionResult> Favorite(string id, CancellationToken token)
    {
        if (await data.GetStation(id, token) is null) return NotFound();
        if (!await db.RiderFavorites.AnyAsync(x => x.UserId == UserId && x.StationId == id, token))
        { db.RiderFavorites.Add(new RiderFavorite { UserId = UserId, StationId = id }); await db.SaveChangesAsync(token); }
        return NoContent();
    }
    [HttpDelete("favorites/{id}")]
    public async Task<IActionResult> RemoveFavorite(string id, CancellationToken token)
    { await db.RiderFavorites.Where(x => x.UserId == UserId && x.StationId == id).ExecuteDeleteAsync(token); return NoContent(); }
    [HttpGet("issues")]
    public async Task<IActionResult> Issues(CancellationToken token) => Ok(await data.WithNames(await db.RiderIssues.AsNoTracking()
        .Where(x => x.UserId == UserId).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(token), token));
    [HttpPost("issues")]
    public async Task<IActionResult> Report(RiderIssueRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length < 5) return BadRequest(new { message = "Please describe the issue." });
        if (await data.GetStation(request.StationId, token) is null) return NotFound();
        if (await db.RiderIssues.CountAsync(x => x.UserId == UserId && x.CreatedAt > DateTime.UtcNow.AddHours(-1), token) >= 10)
            return StatusCode(429, new { message = "Report limit reached. Please try later." });
        var issue = new RiderIssue { UserId = UserId, StationId = request.StationId, Description = request.Description.Trim() };
        db.RiderIssues.Add(issue); await db.SaveChangesAsync(token); await data.Invalidate(token); return Ok(issue);
    }
}

[ApiController, Authorize(Roles = "ADMIN,OPERATOR"), Route("api/rider-issues")]
public class RiderIssuesController(OperationsDbContext db, FleetDataService data, OperationsGate gate, StationStateService states) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> All(CancellationToken token) => Ok(await data.WithNames(await db.RiderIssues.AsNoTracking().OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt).Take(200).ToListAsync(token), token));
    [HttpPost("{id:long}/resolve")]
    public async Task<IActionResult> Resolve(long id, CancellationToken token)
    {
        await gate.Semaphore.WaitAsync(token);
        try
        {
            var issue = await db.RiderIssues.FindAsync([id], token);
            if (issue is null) return NotFound();
            var station = await data.GetStation(issue.StationId, token);
            if (station is null) return Conflict(new { message = "Station is outside the configured source." });
            if (issue.Status != "RESOLVED")
            { issue.Status = "RESOLVED"; issue.ResolvedAt = DateTime.UtcNow; issue.ResolvedBy = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); await states.ReconcileProviderAlerts(station, token); await db.SaveChangesAsync(token); }
            await data.Invalidate(token);
            return Ok(issue);
        }
        finally { gate.Semaphore.Release(); }
    }
}
