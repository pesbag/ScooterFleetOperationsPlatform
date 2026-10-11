using StationApi.Models;
using FleetShared;
using StackExchange.Redis;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using StationApi.DataDbContext;

namespace StationApi.Services;

public class FleetDataService(StationDbContext sql, IMongoDatabase mongo, SmartFleetEngine engine, FleetOptions options, IConfiguration config, IConnectionMultiplexer redis, ILogger<FleetDataService> logger, FleetCacheState cacheState)
{
    public virtual Task<List<StationAnalysis>> GetStations(CancellationToken cancellationToken = default) => LoadStations(null, cancellationToken);

    public virtual async Task<StationAnalysis?> GetStation(string id, CancellationToken token = default) =>
        (await LoadStations(id, token)).FirstOrDefault();

    public Task<List<StationAnalysis>> GetDashboardStations(CancellationToken token = default) =>
        GetCached("stations", () => GetStations(token), token);

    public async Task<StationAnalysis?> GetCachedStation(string id, CancellationToken token = default) =>
        (await GetCached("station:" + id, async () => await GetStation(id, token) is { } station ? [station] : [], token)).FirstOrDefault();

    public async Task Invalidate(CancellationToken token = default)
    {
        cacheState.Generation = Guid.NewGuid().ToString("N");
        try { await redis.GetDatabase().StringSetAsync(FleetCacheContract.RevisionKey, cacheState.Generation).WaitAsync(token); }
        catch (RedisException ex) { logger.LogWarning(ex, "Redis invalidation unavailable; local cache generation changed."); }
    }

    private async Task<List<StationAnalysis>> GetCached(string resource, Func<Task<List<StationAnalysis>>> load, CancellationToken token)
    {
        await cacheState.Gate.WaitAsync(token);
        try
        {
            var cache = redis.GetDatabase();
            string? key = null;
            try
            {
                var revision = await cache.StringGetAsync(FleetCacheContract.RevisionKey).WaitAsync(token);
                var scope = $"{config["DataSource:BaseUrl"]}|{config["DataSource:StationPrefix"]}|{JsonSerializer.Serialize(options)}|{cacheState.Generation}|{revision}";
                key = FleetCacheContract.KeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope))) + ":" + resource;
                var value = await cache.StringGetAsync(key).WaitAsync(token);
                if (value.HasValue)
                {
                    var saved = JsonSerializer.Deserialize<List<StationAnalysis>>(value.ToString());
                    if (saved is not null && saved.All(x => x is not null && x.Station is not null && x.Station.History is not null))
                        return saved.Select(x => engine.Analyze(x.Station, DateTime.UtcNow))
                            .OrderByDescending(x => x.RiskScore).ThenBy(x => x.Station.Name).ToList();
                }
            }
            catch (Exception ex) when (ex is RedisException or JsonException)
            { logger.LogWarning(ex, "Query cache unavailable; reading databases."); }
            var stations = await load();
            // Empty results during initial ingestion and failed database requests are never cached.
            if (key is not null && stations.Count > 0)
            {
                try { await cache.StringSetAsync(key, JsonSerializer.Serialize(stations), TimeSpan.FromSeconds(FleetCacheContract.TtlSeconds)).WaitAsync(token); }
                catch (RedisException ex) { logger.LogWarning(ex, "Query cache write skipped."); }
            }
            return stations;
        }
        finally { cacheState.Gate.Release(); }
    }

    private async Task<List<StationAnalysis>> LoadStations(string? stationId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var prefix = config["DataSource:StationPrefix"]!;
        var query = sql.stationInformation.AsNoTracking().Where(x => x.Station_id.StartsWith(prefix));
        if (stationId is not null) query = query.Where(x => x.Station_id == stationId);
        var stations = await query.ToListAsync(cancellationToken);
        if (stations.Count == 0) return [];
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
            .ToDictionary(x => x.Key, x => x.Where(d => Number(d, "Count") is >= 0 && d.GetValue("RecordedAt", BsonNull.Value).IsBsonDateTime)
                .Select(d => new Reading(d["RecordedAt"].ToUniversalTime(), Number(d, "Count")!.Value)).ToList());
        return stations.Select(s =>
        {
            currentById.TryGetValue(s.Station_id, out var doc);
            DateTime? reported = null;
            var timestampValue = Value(doc, "Last_reported", "last_reported");
            long timestamp = timestampValue.IsInt64 || timestampValue.IsInt32 ? timestampValue.ToInt64() : 0;
            if (timestamp > 0 && timestamp < 253402300800) reported = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
            return engine.Analyze(new(s.Station_id, string.IsNullOrWhiteSpace(s.Name) ? $"Station #{s.Station_id}" : s.Name, s.Capacity,
                Number(doc, "Num_bikes_available", "num_bikes_available"),
                Number(doc, "Num_docs_available", "num_docks_available"), reported,
                Flag(doc, "Is_renting", "is_renting"), Flag(doc, "Is_returning", "is_returning"),
                s.Lat, s.Lon, historyById.GetValueOrDefault(s.Station_id) ?? [],
                Value(doc, "ReceivedAt").IsBsonDateTime ? Value(doc, "ReceivedAt").ToUniversalTime() : null), now);
        }).OrderByDescending(x => x.RiskScore).ThenBy(x => x.Station.Name).ToList();
    }

    private async Task<Dictionary<string, string>> Names(IEnumerable<string> stationIds, CancellationToken token)
    {
        var ids = stationIds.Distinct().ToList();
        return await sql.stationInformation.AsNoTracking().Where(x => ids.Contains(x.Station_id))
            .ToDictionaryAsync(x => x.Station_id, x => string.IsNullOrWhiteSpace(x.Name) ? "Station #" + x.Station_id : x.Name, token);
    }
    public async Task<List<FleetAlert>> WithNames(List<FleetAlert> rows, CancellationToken token)
    {
        var names = await Names(rows.Select(x => x.StationId), token);
        foreach (var row in rows) row.StationName = names.GetValueOrDefault(row.StationId, "Station #" + row.StationId);
        return rows;
    }
    public async Task<List<Recommendation>> WithNames(List<Recommendation> rows, CancellationToken token)
    {
        var names = await Names(rows.SelectMany(x => new[] { x.SourceStationId, x.DestinationStationId }), token);
        foreach (var row in rows) { row.SourceStationName = names.GetValueOrDefault(row.SourceStationId, "Station #" + row.SourceStationId); row.DestinationStationName = names.GetValueOrDefault(row.DestinationStationId, "Station #" + row.DestinationStationId); }
        return rows;
    }
    public async Task<List<RiderIssue>> WithNames(List<RiderIssue> rows, CancellationToken token)
    {
        var names = await Names(rows.Select(x => x.StationId), token);
        foreach (var row in rows) row.StationName = names.GetValueOrDefault(row.StationId, "Station #" + row.StationId);
        return rows;
    }

    private static BsonValue Value(BsonDocument? doc, string key, string? fallback = null) =>
        doc is null ? BsonNull.Value : doc.GetValue(key, fallback is null ? BsonNull.Value : doc.GetValue(fallback, BsonNull.Value));
    private static int? Number(BsonDocument? doc, string key, string? fallback = null)
    {
        var value = Value(doc, key, fallback);
        return (value.IsInt32 || value.IsInt64) && value.ToInt64() is >= int.MinValue and <= int.MaxValue
            ? value.ToInt32() : null;
    }
    private static bool? Flag(BsonDocument? doc, string key, string fallback)
    {
        var value = Value(doc, key, fallback);
        if (value.IsBoolean) return value.AsBoolean;
        return Number(doc, key, fallback) switch { 1 => true, 0 => false, _ => null };
    }
    private static string Text(BsonDocument doc, string key, string fallback) => doc.GetValue(key, doc.GetValue(fallback, "")).AsString;
}
