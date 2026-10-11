using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StationApi.DataDbContext;
using StationApi.Services;
using StationApi.Models;

namespace StationApi.Controllers;

public record StatusRequest(string Status);

[ApiController, Authorize(Roles = "ADMIN,OPERATOR"), Route("api")]
public class OperationsController(OperationsDbContext db, OperationsGate gate, FleetDataService data, StationStateService states, IConfiguration config) : ControllerBase
{
    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts(string? severity, string? status, string? station, CancellationToken token)
    {
        var prefix = config["DataSource:StationPrefix"]!;
        var query = db.Alerts.AsNoTracking().Where(x => x.StationId.StartsWith(prefix));
        if (severity is not null) query = query.Where(x => x.Severity == severity);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (station is not null) query = query.Where(x => x.StationId == station);
        return Ok(await data.WithNames(await query.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(token), token));
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
            if (await data.GetStation(item.StationId, token) is null) return Conflict(new { message = "This station is not part of the configured source." });
            if (item.Status == "RESOLVED") { await data.Invalidate(token); return Ok(item); }
            item.Status = "RESOLVED"; item.ResolvedAt = DateTime.UtcNow;
            item.ResolvedBy = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await db.SaveChangesAsync(token);
            await data.Invalidate(token);
            return Ok(item);
        }
        finally { gate.Semaphore.Release(); }
    }

    [HttpGet("recommendations")]
    public async Task<IActionResult> Recommendations(string? status, CancellationToken token) => Ok(await data.WithNames(await db.Recommendations.AsNoTracking()
        .Where(x => x.SourceStationId.StartsWith(config["DataSource:StationPrefix"]!) && x.DestinationStationId.StartsWith(config["DataSource:StationPrefix"]!) && (status == null || x.Status == status)).OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).ThenByDescending(x => x.Id).Take(500).ToListAsync(token), token));
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
            if (await data.GetStation(item.SourceStationId, token) is null || await data.GetStation(item.DestinationStationId, token) is null)
                return Conflict(new { message = "One of the task stations is not part of the configured source." });
            if (item.Status == request.Status) { await data.Invalidate(token); return Ok(item); }
            if (item.Status is "COMPLETED" or "DISMISSED") return Conflict(new { message = "This task is already closed." });
            if (request.Status == "IN_PROGRESS")
            {
                var source = await data.GetStation(item.SourceStationId, token);
                var destination = await data.GetStation(item.DestinationStationId, token);
                if (source is null || destination is null || source.Stale || destination.Stale || source.Station.Renting != true
                    || destination.Station.Returning != true || source.Station.Count < item.Quantity
                    || destination.Station.AvailableDocks < item.Quantity || destination.Station.Capacity - destination.Station.Count < item.Quantity)
                    return Conflict(new { message = "Station availability has changed. Dismiss this task and wait for a new recommendation." });
            }
            var measurement = await db.TaskMeasurements.FindAsync([item.Id], token);
            if (measurement is null) { measurement = new TaskMeasurement { RecommendationId = item.Id }; db.TaskMeasurements.Add(measurement); }
            if (request.Status == "IN_PROGRESS" && measurement.StartedAt is null)
            {
                measurement.StartedAt = DateTime.UtcNow;
                var destination = await data.GetStation(item.DestinationStationId, token);
                measurement.DestinationBefore = destination is { Stale: false } ? destination.Station.Count : null;
            }
            if (request.Status == "COMPLETED")
            {
                measurement.CompletedAt ??= DateTime.UtcNow;
                foreach (var idToRefresh in new[] { item.SourceStationId, item.DestinationStationId })
                    if (await data.GetStation(idToRefresh, token) is { } current) await states.ReconcileProviderAlerts(current, token);
            }
            item.Status = request.Status; item.UpdatedAt = DateTime.UtcNow;
            item.UpdatedBy = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            // Completion records the operator's action. Telemetry remains owned by the data source.
            await db.SaveChangesAsync(token);
            await data.Invalidate(token);
            return Ok(item);
        }
        finally { gate.Semaphore.Release(); }
    }
}
