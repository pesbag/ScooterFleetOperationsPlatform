using StationApi.Models;
namespace StationApi.Services;

public class FleetOptions
{
    public double LowRatio { get; set; } = .25;
    public double HighRatio { get; set; } = .75;
    public double TargetRatio { get; set; } = .5;
    public int WarningMinutes { get; set; } = 120;
    public int CriticalMinutes { get; set; } = 30;
    public int StaleMinutes { get; set; } = 10;
    public int HistoryMinutes { get; set; } = 120;
    public int MaxMove { get; set; } = 12;
    public int RecommendationCooldownMinutes { get; set; } = 15;

    public bool IsValid() => LowRatio > 0 && LowRatio < TargetRatio && TargetRatio < HighRatio
        && HighRatio < 1 && CriticalMinutes > 0 && WarningMinutes > CriticalMinutes
        && StaleMinutes > 0 && HistoryMinutes >= 10 && MaxMove > 0 && RecommendationCooldownMinutes > 0;
}

public record Reading(DateTime At, int Count);
public record StationInput(string Id, string Name, int Capacity, int? Count, int? AvailableDocks,
    DateTime? ReportedAt, bool? Renting, bool? Returning, double Lat, double Lon, List<Reading> History, DateTime? ReceivedAt = null);
public record StationAnalysis(StationInput Station, double? Utilization, int RiskScore, string RiskLevel,
    string Status, double? RatePerHour, double? ShortageMinutes, double? FullMinutes, bool Stale, string Explanation, string DataState = "CURRENT")
{
    public int OpenIssueCount { get; init; }
    public string MaintenanceStatus { get; init; } = "CLEAR";
    public string OperationalStatus { get; init; } = "UNKNOWN";
}
public record MoveSuggestion(string SourceStationId, string DestinationStationId, int Quantity, string Priority, string Reason);

// Pure deterministic calculations: no database, network, clock or UI dependencies.
public class SmartFleetEngine(FleetOptions options)
{
    public StationAnalysis Analyze(StationInput station, DateTime now)
    {
        bool stale = station.ReportedAt is null || station.ReportedAt < now.AddMinutes(-options.StaleMinutes)
            || station.ReportedAt > now.AddMinutes(2);
        bool missing = station.Count is null || station.AvailableDocks is null || station.ReportedAt is null
            || station.Renting is null || station.Returning is null;
        bool invalid = station.Capacity < 0 || station.Count < 0 || (station.Capacity > 0 && station.Count > station.Capacity)
            || station.AvailableDocks < 0 || (station.Capacity > 0 && station.AvailableDocks > station.Capacity)
            || station.ReportedAt > now.AddMinutes(2);
        if (missing || invalid || stale)
        {
            string state = invalid ? "INVALID" : missing ? "MISSING" : "OUTDATED";
            string message = invalid ? "Station data is invalid. Verify the source before moving bikes."
                : missing ? "Required station data is missing. This does not mean the station is empty."
                : "The last reading is outdated. Current availability is unknown.";
            return new(station, null, 70, "HIGH", "WARNING", null, null, null, true, message, state);
        }
        double? ratio = station.Capacity > 0 ? (double)station.Count!.Value / station.Capacity : null;
        var history = station.History.Where(x => x.At >= now.AddMinutes(-options.HistoryMinutes)
            && x.At <= now && x.Count >= 0 && (station.Capacity == 0 || x.Count <= station.Capacity))
            .GroupBy(x => x.At).Select(x => x.Last()).OrderBy(x => x.At).ToList();
        double? rate = null;
        if (!stale && history.Count >= 2)
        {
            double hours = (history[^1].At - history[0].At).TotalHours;
            if (hours >= 5d / 60 && history[^1].At >= now.AddMinutes(-options.StaleMinutes))
                rate = (history[^1].Count - history[0].Count) / hours;
        }
        double? shortage = !stale && station.Count == 0 ? 0 : rate < -.01 ? station.Count / -rate * 60 : null;
        double? full = station.Capacity == 0 ? null : !stale && station.Count == station.Capacity ? 0 : rate > .01 ? (station.Capacity - station.Count) / rate * 60 : null;
        int score = station.Count == 0 ? 85 : ratio <= .1 || ratio >= .9 ? 85 : ratio < options.LowRatio || ratio > options.HighRatio ? 55 : 15;
        double? earliest = shortage ?? full;
        if (earliest <= options.CriticalMinutes) score = Math.Max(score, 95);
        else if (earliest <= options.WarningMinutes) score = Math.Max(score, 75);
        if (station.Renting != true || station.Returning != true) score = Math.Max(score, 70);
        if (stale) score = Math.Max(score, 70);
        string status = score >= 85 ? "CRITICAL" : score >= 40 ? "WARNING" : "HEALTHY";
        string level = score >= 85 ? "CRITICAL" : score >= 70 ? "HIGH" : score >= 40 ? "MEDIUM" : "LOW";
        string explanation = stale ? "Data is missing or outdated. Verify the station before moving bikes."
            : shortage is not null ? $"Expected to run out in approximately {DurationText.Format(shortage.Value)} at the recent net rate."
            : full is not null ? $"Expected to reach capacity in approximately {DurationText.Format(full.Value)} at the recent net rate."
            : rate is null ? "Not enough recent history for a prediction; risk uses current availability."
            : "Availability is stable over the recent observation window.";
        if (station.Renting != true || station.Returning != true) explanation += " Station rental or return service is unavailable.";
        if (station.Capacity == 0) explanation += " Station capacity is not published. Utilization, full-capacity predictions and transfer recommendations are unavailable.";
        return new(station, ratio is null ? null : Math.Round(ratio.Value * 100, 1), score, level, status,
            rate is null ? null : Math.Round(rate.Value, 2), shortage, full, stale, explanation);
    }

    public List<MoveSuggestion> Recommend(IReadOnlyList<StationAnalysis> stations, ISet<string> reservedStations)
    {
        var eligible = stations.Where(x => !x.Stale && x.Station.Capacity > 0 && x.Station.Renting == true
            && x.Station.Returning == true && !reservedStations.Contains(x.Station.Id)).ToList();
        var available = eligible.ToDictionary(x => x.Station.Id,
            x => Math.Max(0, x.Station.Count!.Value - (int)Math.Ceiling(x.Station.Capacity * options.TargetRatio)));
        var needs = eligible.ToDictionary(x => x.Station.Id,
            x => Math.Max(0, (int)Math.Floor(x.Station.Capacity * options.TargetRatio) - x.Station.Count!.Value));
        var result = new List<MoveSuggestion>();
        foreach (var receiver in eligible.OrderByDescending(x => x.RiskScore))
        {
            foreach (var donor in eligible.Where(x => x.Station.Id != receiver.Station.Id)
                         .OrderByDescending(x => x.RiskScore).ThenBy(x => Distance(x.Station, receiver.Station)))
            {
                bool shortage = receiver.Station.Count < receiver.Station.Capacity * options.LowRatio
                    || receiver.ShortageMinutes <= options.WarningMinutes;
                bool crowded = donor.Station.Count > donor.Station.Capacity * options.HighRatio
                    || donor.FullMinutes <= options.WarningMinutes;
                if (!shortage && !crowded) continue;
                // Do not take bikes from a donor already trending toward a shortage.
                if (donor.ShortageMinutes <= options.WarningMinutes || receiver.FullMinutes <= options.WarningMinutes) continue;
                int quantity = Math.Min(options.MaxMove, Math.Min(available[donor.Station.Id], needs[receiver.Station.Id]));
                quantity = Math.Min(quantity, Math.Max(0, receiver.Station.AvailableDocks!.Value));
                if (quantity <= 0) continue;
                result.Add(new(donor.Station.Id, receiver.Station.Id, quantity,
                    Math.Max(receiver.RiskScore, donor.RiskScore) >= 85 ? "CRITICAL" : "HIGH",
                    $"Move {quantity} bikes from {donor.Station.Name} to {receiver.Station.Name}. "
                    + (shortage ? receiver.Explanation : donor.Explanation)));
                available[donor.Station.Id] -= quantity;
                needs[receiver.Station.Id] -= quantity;
                // One active operation per station keeps operator instructions unambiguous.
                available[receiver.Station.Id] = 0;
                needs[donor.Station.Id] = 0;
                available[donor.Station.Id] = 0;
                needs[receiver.Station.Id] = 0;
                break;
            }
        }
        return result;
    }

    private static double Distance(StationInput a, StationInput b) => Math.Pow(a.Lat - b.Lat, 2) + Math.Pow(a.Lon - b.Lon, 2);
}
