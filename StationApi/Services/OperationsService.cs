using Microsoft.EntityFrameworkCore;
using StationApi.DataDbContext;
using StationApi.Models;

namespace StationApi.Services;

// One API instance owns the operational writer. The gate also serializes operator updates.
public sealed class OperationsGate { public SemaphoreSlim Semaphore { get; } = new(1, 1); }

public class OperationsService(OperationsDbContext db, FleetDataService data, SmartFleetEngine engine,
    FleetOptions options, OperationsGate gate)
{
    public async Task Refresh(CancellationToken token)
    {
        await gate.Semaphore.WaitAsync(token);
        try
        {
            var stations = await data.GetStations(token);
            var now = DateTime.UtcNow;
            var alerts = await db.Alerts.Where(x => x.Status == "ACTIVE").ToListAsync(token);
            var recentlyResolved = await db.Alerts.Where(x => x.ResolvedAt > now.AddMinutes(-options.RecommendationCooldownMinutes))
                .Select(x => x.StationId).ToListAsync(token);
            foreach (var station in stations)
            {
                var active = alerts.FirstOrDefault(x => x.StationId == station.Station.Id);
                if (station.Status == "HEALTHY")
                {
                    if (active is not null) { active.Status = "RESOLVED"; active.ResolvedAt = now; }
                    continue;
                }
                if (active is null && recentlyResolved.Contains(station.Station.Id)) continue;
                if (active is null)
                {
                    active = new FleetAlert { StationId = station.Station.Id };
                    db.Alerts.Add(active);
                }
                active.Severity = station.Status;
                active.Title = station.Stale ? "Station data needs attention" : $"{station.Station.Name}: availability risk";
                active.Description = station.Explanation;
            }
            var pending = await db.Recommendations.Where(x => x.Status == "PENDING" || x.Status == "IN_PROGRESS").ToListAsync(token);
            var byId = stations.ToDictionary(x => x.Station.Id);
            foreach (var item in pending.Where(x => x.Status == "PENDING"))
            {
                if (!byId.TryGetValue(item.SourceStationId, out var source) || !byId.TryGetValue(item.DestinationStationId, out var target)
                    || source.Stale || target.Stale || !source.Station.Renting || !target.Station.Returning
                    || source.Station.Count < item.Quantity || target.Station.AvailableDocks < item.Quantity
                    || target.Station.Capacity - target.Station.Count < item.Quantity)
                {
                    item.Status = "DISMISSED"; item.UpdatedAt = now;
                    item.Reason += " Automatically dismissed because current availability no longer supports this move.";
                }
            }
            var recent = await db.Recommendations.Where(x => x.UpdatedAt > now.AddMinutes(-options.RecommendationCooldownMinutes)).ToListAsync(token);
            var reserved = pending.Where(x => x.Status is "PENDING" or "IN_PROGRESS").Concat(recent)
                .SelectMany(x => new[] { x.SourceStationId, x.DestinationStationId }).ToHashSet();
            foreach (var move in engine.Recommend(stations, reserved))
                db.Recommendations.Add(new Recommendation { SourceStationId = move.SourceStationId,
                    DestinationStationId = move.DestinationStationId, Quantity = move.Quantity, Priority = move.Priority, Reason = move.Reason });
            await db.SaveChangesAsync(token);
        }
        finally { gate.Semaphore.Release(); }
    }
}

public class FleetAnalysisWorker(IServiceScopeFactory scopes, ILogger<FleetAnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OperationsService>().Refresh(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Fleet analysis could not refresh; retrying in 30 seconds."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
