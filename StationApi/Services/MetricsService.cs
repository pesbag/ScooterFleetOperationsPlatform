using Microsoft.EntityFrameworkCore;
using StationApi.DataDbContext;
using StationApi.Models;
namespace StationApi.Services;

public class MetricsService(OperationsDbContext db, FleetOptions options)
{
    public static double EmptyMinutes(IEnumerable<Reading> history, int maxGapMinutes)
    {
        var readings = history.OrderBy(x => x.At).DistinctBy(x => x.At).ToList();
        double total = 0;
        for (int i = 1; i < readings.Count; i++)
        {
            var gap = (readings[i].At - readings[i - 1].At).TotalMinutes;
            if (gap > 0 && gap <= maxGapMinutes && readings[i - 1].Count == 0 && readings[i].Count == 0) total += gap;
        }
        return Math.Round(total, 1);
    }
    public async Task Observe(List<StationAnalysis> stations, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var byId = stations.ToDictionary(x => x.Station.Id);
        var ids = byId.Keys.ToList();
        var samples = await db.ForecastSamples.Where(x => ids.Contains(x.StationId) && x.DueAt >= now.AddHours(-2)).ToListAsync(token);
        foreach (var sample in samples.Where(x => x.ActualCount == null && x.DueAt <= now))
        {
            var reading = byId[sample.StationId].Station.History.Where(x => x.At >= sample.DueAt && x.At <= sample.DueAt.AddMinutes(5) && x.At <= now && x.Count >= 0
                && (byId[sample.StationId].Station.Capacity == 0 || x.Count <= byId[sample.StationId].Station.Capacity))
                .OrderBy(x => x.At).FirstOrDefault();
            if (reading is not null) { sample.ActualCount = reading.Count; sample.ObservedAt = reading.At; }
        }
        foreach (var station in stations.Where(x => !x.Stale && x.RatePerHour is not null))
        {
            if (samples.Any(x => x.StationId == station.Station.Id && x.IssuedAt > now.AddMinutes(-30))) continue;
            // Forecast a fixed 30-minute horizon from the actual source reading, not the polling clock.
            var issued = station.Station.ReportedAt!.Value;
            var predicted = Math.Max(0, station.Station.Count!.Value + station.RatePerHour!.Value * .5);
            if (station.Station.Capacity > 0) predicted = Math.Min(station.Station.Capacity, predicted);
            db.ForecastSamples.Add(new ForecastSample { StationId = station.Station.Id, IssuedAt = issued,
                DueAt = issued.AddMinutes(30), PredictedCount = predicted });
        }
        var observations = await (from m in db.TaskMeasurements join r in db.Recommendations on m.RecommendationId equals r.Id
            where m.CompletedAt != null && m.ObservedAt == null && m.CompletedAt > now.AddDays(-1)
                && ids.Contains(r.DestinationStationId)
            select new { Metric = m, r.DestinationStationId }).ToListAsync(token);
        foreach (var item in observations)
        {
            var reading = byId[item.DestinationStationId].Station.History
                .Where(x => x.At >= item.Metric.CompletedAt!.Value.AddMinutes(15) && x.At <= item.Metric.CompletedAt.Value.AddMinutes(20) && x.At <= now && x.Count >= 0)
                .OrderBy(x => x.At).FirstOrDefault();
            if (reading is not null) { item.Metric.DestinationAfter = reading.Count; item.Metric.ObservedAt = reading.At; }
        }
        await db.SaveChangesAsync(token);
    }
    public async Task<object> Summary(List<StationAnalysis> stations, CancellationToken token)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var ids = stations.Select(x => x.Station.Id).ToList();
        var tasks = await db.Recommendations.AsNoTracking().Where(x => x.CreatedAt >= since && ids.Contains(x.SourceStationId) && ids.Contains(x.DestinationStationId)).ToListAsync(token);
        var taskIds = tasks.Select(x => x.Id).ToList();
        var measurements = await db.TaskMeasurements.AsNoTracking().Where(x => taskIds.Contains(x.RecommendationId)).ToListAsync(token);
        var samples = await db.ForecastSamples.AsNoTracking().Where(x => x.IssuedAt >= since && ids.Contains(x.StationId)).ToListAsync(token);
        var matched = samples.Where(x => x.ActualCount != null).ToList();
        var response = measurements.Where(x => x.StartedAt != null).Select(x => (x.StartedAt!.Value - tasks.Single(t => t.Id == x.RecommendationId).CreatedAt).TotalMinutes).ToList();
        var changes = measurements.Where(x => x.DestinationBefore != null && x.DestinationAfter != null).Select(x => x.DestinationAfter!.Value - x.DestinationBefore!.Value).ToList();
        return new {
            historyMinutes = options.HistoryMinutes,
            emptyMinutes = stations.Any(x => x.Station.History.Count >= 2) ? (double?)stations.Sum(x => EmptyMinutes(x.Station.History.Where(r => r.At <= DateTime.UtcNow), options.StaleMinutes)) : null,
            stationsWithHistory = stations.Count(x => x.Station.History.Count >= 2),
            taskCount = tasks.Count, completed = tasks.Count(x => x.Status == "COMPLETED"),
            completionPercent = tasks.Count == 0 ? (double?)null : Math.Round(100d * tasks.Count(x => x.Status == "COMPLETED") / tasks.Count, 1),
            responseMinutes = response.Count == 0 ? (double?)null : Math.Round(response.Average(), 1), responseSamples = response.Count,
            forecastError = matched.Count == 0 ? (double?)null : Math.Round(matched.Average(x => Math.Abs(x.PredictedCount - x.ActualCount!.Value)), 2),
            forecastSamples = matched.Count, forecastsIssued = samples.Count,
            destinationChange = changes.Count == 0 ? (double?)null : Math.Round(changes.Average(), 1), changeSamples = changes.Count,
            stations = stations.Select(x => new { x.Station.Id, x.Station.Name, emptyMinutes = EmptyMinutes(x.Station.History, options.StaleMinutes), readings = x.Station.History.Count })
        };
    }
}
