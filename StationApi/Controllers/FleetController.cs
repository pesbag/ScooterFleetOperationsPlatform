using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StationApi.DataDbContext;
using StationApi.Services;

namespace StationApi.Controllers;

[ApiController, Authorize, Route("api")]
public class FleetController(FleetDataService data, OperationsDbContext db, IConfiguration config) : ControllerBase
{
    [HttpGet("stations")]
    public async Task<IActionResult> Stations(CancellationToken token) => Ok(await data.GetStations(token));

    [HttpGet("stations/{id}")]
    public async Task<IActionResult> Station(string id, CancellationToken token)
    {
        var station = (await data.GetStations(token)).FirstOrDefault(x => x.Station.Id == id);
        if (station is null) return NotFound(new { message = "Station not found." });
        return Ok(new { analysis = station,
            alerts = await db.Alerts.AsNoTracking().Where(x => x.StationId == id && x.Status == "ACTIVE").ToListAsync(token),
            recommendations = await db.Recommendations.AsNoTracking().Where(x => (x.SourceStationId == id || x.DestinationStationId == id)
                && (x.Status == "PENDING" || x.Status == "IN_PROGRESS")).ToListAsync(token) });
    }

    [HttpGet("stations/{id}/history")]
    public async Task<IActionResult> History(string id, CancellationToken token)
    {
        var station = (await data.GetStations(token)).FirstOrDefault(x => x.Station.Id == id);
        return station is null ? NotFound(new { message = "Station not found." }) : Ok(station.Station.History.OrderBy(x => x.At));
    }

    [HttpGet("stations/{id}/status"), HttpGet("stations/{id}/statistics")]
    public async Task<IActionResult> Status(string id, CancellationToken token)
    {
        var station = (await data.GetStations(token)).FirstOrDefault(x => x.Station.Id == id);
        return station is null ? NotFound(new { message = "Station not found." }) : Ok(station);
    }

    [HttpGet("dashboard"), HttpGet("fleet"), HttpGet("fleet/statistics"), HttpGet("fleet/distribution")]
    public async Task<IActionResult> Dashboard(CancellationToken token)
    {
        var stations = await data.GetStations(token);
        return Ok(new { dataMode = config["DataSource:Mode"] ?? "Live", updatedAt = DateTime.UtcNow,
            healthy = stations.Count(x => x.Status == "HEALTHY"), warning = stations.Count(x => x.Status == "WARNING"),
            critical = stations.Count(x => x.Status == "CRITICAL"), stale = stations.Count(x => x.Stale),
            totalScooters = stations.Sum(x => x.Station.Count), availableCapacity = stations.Sum(x => Math.Max(0, x.Station.AvailableDocks)),
            activeAlerts = await db.Alerts.CountAsync(x => x.Status == "ACTIVE", token),
            pendingRecommendations = await db.Recommendations.CountAsync(x => x.Status == "PENDING" || x.Status == "IN_PROGRESS", token),
            stations, alerts = await db.Alerts.AsNoTracking().Where(x => x.Status == "ACTIVE").OrderBy(x => x.Severity).Take(6).ToListAsync(token),
            recommendations = await db.Recommendations.AsNoTracking().Where(x => x.Status == "PENDING" || x.Status == "IN_PROGRESS")
                .OrderBy(x => x.Priority).Take(6).ToListAsync(token) });
    }
}
