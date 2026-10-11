using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StationApi.DataDbContext;
using StationApi.Services;

namespace StationApi.Controllers;

[ApiController, Authorize(Roles = "ADMIN,OPERATOR"), Route("api")]
public class FleetController(FleetDataService data, OperationsDbContext db, IConfiguration config, StationStateService states) : ControllerBase
{
    [HttpGet("stations")]
    public async Task<IActionResult> Stations(CancellationToken token) => Ok(await states.Enrich(await data.GetDashboardStations(token), token));

    [HttpGet("stations/{id}")]
    public async Task<IActionResult> Station(string id, CancellationToken token)
    {
        var station = await data.GetCachedStation(id, token);
        if (station is null) return NotFound(new { message = "Station not found." });
        station = (await states.Enrich([station], token))[0];
        return Ok(new { analysis = station,
            alerts = await data.WithNames(await db.Alerts.AsNoTracking().Where(x => x.StationId == id && x.Status == "ACTIVE").ToListAsync(token), token),
            recommendations = await data.WithNames(await db.Recommendations.AsNoTracking().Where(x => (x.SourceStationId == id || x.DestinationStationId == id)
                && (x.Status == "PENDING" || x.Status == "IN_PROGRESS")).ToListAsync(token), token) });
    }

    [HttpGet("stations/{id}/history")]
    public async Task<IActionResult> History(string id, CancellationToken token)
    {
        var station = await data.GetCachedStation(id, token);
        return station is null ? NotFound(new { message = "Station not found." }) : Ok(station.Station.History.OrderBy(x => x.At));
    }

    [HttpGet("stations/{id}/status"), HttpGet("stations/{id}/statistics")]
    public async Task<IActionResult> Status(string id, CancellationToken token)
    {
        var station = await data.GetCachedStation(id, token);
        return station is null ? NotFound(new { message = "Station not found." }) : Ok((await states.Enrich([station], token))[0]);
    }

    [HttpGet("dashboard"), HttpGet("fleet"), HttpGet("fleet/statistics"), HttpGet("fleet/distribution")]
    public async Task<IActionResult> Dashboard(CancellationToken token)
    {
        var stations = await states.Enrich(await data.GetDashboardStations(token), token);
        var ids = stations.Select(x => x.Station.Id).ToList();
        return Ok(new { dataMode = "Live", provider = config["DataSource:Name"], updatedAt = DateTime.UtcNow,
            healthy = stations.Count(x => x.Status == "HEALTHY"), warning = stations.Count(x => x.Status == "WARNING"),
            critical = stations.Count(x => x.Status == "CRITICAL"), stale = stations.Count(x => x.Stale),
            availableBikes = stations.Any(x => !x.Stale) ? stations.Where(x => !x.Stale).Sum(x => x.Station.Count) : null,
            availableCapacity = stations.Any(x => !x.Stale) ? stations.Where(x => !x.Stale).Sum(x => x.Station.AvailableDocks) : null,
            currentStations = stations.Count(x => !x.Stale), totalStations = stations.Count,
            lastReportedAt = stations.Select(x => x.Station.ReportedAt).Max(),
            lastReceivedAt = stations.Select(x => x.Station.ReceivedAt).Max(),
            activeAlerts = await db.Alerts.CountAsync(x => x.Status == "ACTIVE" && ids.Contains(x.StationId), token),
            pendingRecommendations = await db.Recommendations.CountAsync(x => (x.Status == "PENDING" || x.Status == "IN_PROGRESS") && ids.Contains(x.SourceStationId) && ids.Contains(x.DestinationStationId), token),
            stations, alerts = await data.WithNames(await db.Alerts.AsNoTracking().Where(x => x.Status == "ACTIVE" && ids.Contains(x.StationId)).OrderBy(x => x.Severity).Take(6).ToListAsync(token), token),
            recommendations = await data.WithNames(await db.Recommendations.AsNoTracking().Where(x => (x.Status == "PENDING" || x.Status == "IN_PROGRESS") && ids.Contains(x.SourceStationId) && ids.Contains(x.DestinationStationId))
                .OrderBy(x => x.Priority).Take(6).ToListAsync(token), token) });
    }
}
