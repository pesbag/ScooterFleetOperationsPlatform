using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StationApi.DataDbContext;
using StationApi.Services;

namespace StationApi.Controllers;

public record StatusRequest(string Status);

[ApiController, Authorize(Roles = "ADMIN,OPERATOR"), Route("api")]
public class OperationsController(OperationsDbContext db, OperationsGate gate, FleetDataService data) : ControllerBase
{
    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts(string? severity, string? status, string? station, CancellationToken token)
    {
        var query = db.Alerts.AsNoTracking().AsQueryable();
        if (severity is not null) query = query.Where(x => x.Severity == severity);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (station is not null) query = query.Where(x => x.StationId == station);
        return Ok(await query.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(token));
    }
    [HttpGet("alerts/{id:long}")]
    public async Task<IActionResult> Alert(long id) => await db.Alerts.FindAsync(id) is { } item ? Ok(item) : NotFound();

    [HttpPatch("alerts/{id:long}")]
    public async Task<IActionResult> Resolve(long id, StatusRequest request, CancellationToken token)
    {
        if (request.Status != "RESOLVED") return BadRequest(new { message = "Alert status must be RESOLVED." });
        await gate.Semaphore.WaitAsync(token);
        try
        {
            var item = await db.Alerts.FindAsync([id], token);
            if (item is null) return NotFound();
            if (item.Status == "RESOLVED") return Ok(item);
            item.Status = "RESOLVED"; item.ResolvedAt = DateTime.UtcNow;
            item.ResolvedBy = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await db.SaveChangesAsync(token);
            return Ok(item);
        }
        finally { gate.Semaphore.Release(); }
    }

    [HttpGet("recommendations")]
    public async Task<IActionResult> Recommendations(string? status, CancellationToken token) => Ok(await db.Recommendations.AsNoTracking()
        .Where(x => status == null || x.Status == status).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(token));
    [HttpGet("recommendations/{id:long}")]
    public async Task<IActionResult> Recommendation(long id) => await db.Recommendations.FindAsync(id) is { } item ? Ok(item) : NotFound();

    [HttpPatch("recommendations/{id:long}")]
    public async Task<IActionResult> Update(long id, StatusRequest request, CancellationToken token)
    {
        if (request.Status is not ("IN_PROGRESS" or "COMPLETED" or "DISMISSED")) return BadRequest(new { message = "Invalid task status." });
        await gate.Semaphore.WaitAsync(token);
        try
        {
            var item = await db.Recommendations.FindAsync([id], token);
            if (item is null) return NotFound();
            if (item.Status == request.Status) return Ok(item);
            if (item.Status is "COMPLETED" or "DISMISSED") return Conflict(new { message = "This task is already closed." });
            if (request.Status == "IN_PROGRESS")
            {
                var stations = await data.GetStations(token);
                var source = stations.FirstOrDefault(x => x.Station.Id == item.SourceStationId);
                var destination = stations.FirstOrDefault(x => x.Station.Id == item.DestinationStationId);
                if (source is null || destination is null || source.Stale || destination.Stale || !source.Station.Renting
                    || !destination.Station.Returning || source.Station.Count < item.Quantity
                    || destination.Station.AvailableDocks < item.Quantity || destination.Station.Capacity - destination.Station.Count < item.Quantity)
                    return Conflict(new { message = "Station availability has changed. Dismiss this task and wait for a new recommendation." });
            }
            item.Status = request.Status; item.UpdatedAt = DateTime.UtcNow;
            item.UpdatedBy = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            // Completion records the operator's action. Telemetry remains owned by the data source.
            await db.SaveChangesAsync(token);
            return Ok(item);
        }
        finally { gate.Semaphore.Release(); }
    }
}
