using Microsoft.EntityFrameworkCore;
using StationApi.DataDbContext;
namespace StationApi.Services;

// Provider risk, maintenance issues and work completion are separate states.
public class StationStateService(OperationsDbContext db)
{
    public static StationAnalysis Apply(StationAnalysis analysis, int openAlerts, int openReports) => analysis with {
        OpenIssueCount = openAlerts + openReports,
        MaintenanceStatus = openReports > 0 ? "OPEN_ISSUES" : "CLEAR",
        OperationalStatus = openReports > 0 ? "MAINTENANCE" : analysis.Stale ? "UNKNOWN"
            : analysis.Station.Renting != true || analysis.Station.Returning != true ? "SERVICE_UNAVAILABLE"
            : openAlerts > 0 || analysis.Status != "HEALTHY" ? "ATTENTION" : "ACTIVE"
    };
    public async Task ReconcileProviderAlerts(StationAnalysis station, CancellationToken token)
    {
        if (station.Stale || station.Status != "HEALTHY") return;
        var alerts = await db.Alerts.Where(x => x.StationId == station.Station.Id && x.Status == "ACTIVE"
            && (x.Title == station.Station.Name + ": availability risk" || x.Title == "Station data needs attention")).ToListAsync(token);
        foreach (var alert in alerts) { alert.Status = "RESOLVED"; alert.ResolvedAt = DateTime.UtcNow; }
        // Caller saves these changes with the task completion in the same SaveChanges transaction.
    }
    public async Task<List<StationAnalysis>> Enrich(List<StationAnalysis> stations, CancellationToken token)
    {
        var ids = stations.Select(x => x.Station.Id).ToList();
        var alerts = await db.Alerts.AsNoTracking().Where(x => ids.Contains(x.StationId) && x.Status == "ACTIVE")
            .GroupBy(x => x.StationId).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, token);
        var reports = await db.RiderIssues.AsNoTracking().Where(x => ids.Contains(x.StationId) && x.Status == "OPEN")
            .GroupBy(x => x.StationId).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, token);
        return stations.Select(x => Apply(x, alerts.GetValueOrDefault(x.Station.Id), reports.GetValueOrDefault(x.Station.Id))).ToList();
    }
}
