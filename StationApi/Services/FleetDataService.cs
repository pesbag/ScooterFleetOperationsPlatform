using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using StationApi.DataDbContext;

namespace StationApi.Services;

public class FleetDataService(StationDbContext sql, IMongoDatabase mongo, SmartFleetEngine engine, FleetOptions options, IConfiguration config)
{
    public async Task<List<StationAnalysis>> GetStations(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        bool demo = string.Equals(config["DataSource:Mode"], "Demo", StringComparison.OrdinalIgnoreCase);
        var stations = await sql.stationInformation.AsNoTracking()
            .Where(x => demo ? x.Station_id.StartsWith("demo-") : !x.Station_id.StartsWith("demo-"))
            .ToListAsync(cancellationToken);
        var ids = stations.Select(x => x.Station_id).ToList();
        var latest = await mongo.GetCollection<BsonDocument>("StationsStatus")
            .Find(Builders<BsonDocument>.Filter.In("Station_id", ids) | Builders<BsonDocument>.Filter.In("station_id", ids))
            .ToListAsync(cancellationToken);
        var history = await mongo.GetCollection<BsonDocument>("StationHistory")
            .Find(Builders<BsonDocument>.Filter.Gte("RecordedAt", now.AddMinutes(-options.HistoryMinutes))
                & Builders<BsonDocument>.Filter.In("StationId", ids))
            .ToListAsync(cancellationToken);
        var currentById = latest.GroupBy(x => Text(x, "Station_id", "station_id")).ToDictionary(x => x.Key, x => x.Last());
        var historyById = history.GroupBy(x => Text(x, "StationId", "station_id"))
            .ToDictionary(x => x.Key, x => x.Select(d => new Reading(d["RecordedAt"].ToUniversalTime(), Number(d, "Count"))).ToList());
        return stations.Select(s =>
        {
            currentById.TryGetValue(s.Station_id, out var doc);
            DateTime? reported = null;
            long timestamp = doc is null ? 0 : Value(doc, "Last_reported", "last_reported").ToInt64();
            if (timestamp > 0 && timestamp < 253402300800) reported = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
            return engine.Analyze(new(s.Station_id, s.Name, s.Capacity,
                Number(doc, "Num_bikes_available", "num_bikes_available"),
                Number(doc, "Num_docs_available", "num_docks_available"), reported,
                Number(doc, "Is_renting", "is_renting") == 1, Number(doc, "Is_returning", "is_returning") == 1,
                s.Lat, s.Lon, historyById.GetValueOrDefault(s.Station_id) ?? []), now);
        }).OrderByDescending(x => x.RiskScore).ThenBy(x => x.Station.Name).ToList();
    }

    private static BsonValue Value(BsonDocument? doc, string key, string? fallback = null) =>
        doc is null ? new BsonInt32(0) : doc.GetValue(key, fallback is null ? new BsonInt32(0) : doc.GetValue(fallback, 0));
    private static int Number(BsonDocument? doc, string key, string? fallback = null)
    {
        var value = Value(doc, key, fallback);
        return value.IsBoolean ? (value.AsBoolean ? 1 : 0) : value.ToInt32();
    }
    private static string Text(BsonDocument doc, string key, string fallback) => doc.GetValue(key, doc.GetValue(fallback, "")).AsString;
}
