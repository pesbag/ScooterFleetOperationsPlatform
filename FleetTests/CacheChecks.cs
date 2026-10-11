using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using StationApi.Services;

// In-memory Redis stand-in: verifies the actual dashboard cache flow without Docker.
public static class CacheChecks
{
    public static async Task Run()
    {
        var stored = new Dictionary<string, RedisValue>();
        string lastQueryKey = "";
        bool unavailable = false;
        Expiration ttl = default;
        var keys = new List<string>();
        var database = DispatchProxy.Create<IDatabase, RedisProxy>();
        ((RedisProxy)database).Call = (method, args) =>
        {
            if (method.Name == "StringGetAsync")
            {
                keys.Add(args[0]!.ToString()!);
                if (args[0]!.ToString() != FleetShared.FleetCacheContract.RevisionKey) lastQueryKey = args[0]!.ToString()!;
                return unavailable ? Task.FromException<RedisValue>(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Test outage")) : Task.FromResult(stored.GetValueOrDefault(args[0]!.ToString()!, RedisValue.Null));
            }
            if (method.Name == "StringSetAsync")
            {
                if (unavailable) return Task.FromException<bool>(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Test outage"));
                stored[args[0]!.ToString()!] = (RedisValue)args[1]!;
                if (args[0]!.ToString() != FleetShared.FleetCacheContract.RevisionKey) ttl = args.OfType<Expiration>().Single();
                return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        };
        var redis = DispatchProxy.Create<IConnectionMultiplexer, RedisProxy>();
        ((RedisProxy)redis).Call = (method, args) => method.Name == "GetDatabase" ? database : throw new NotSupportedException(method.Name);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataSource:BaseUrl"] = "https://fixture.invalid/", ["DataSource:StationPrefix"] = "citibike:" }).Build();
        var options = new FleetOptions();
        var engine = new SmartFleetEngine(options);
        var station = new StationInput("citibike:test", "Test", 40, 10, 30, DateTime.UtcNow, true, true, 0, 0, []);
        var service = new DataFixture(engine, options, config, redis) { Rows = [engine.Analyze(station, DateTime.UtcNow)] };
        await service.GetDashboardStations();
        await service.GetDashboardStations();
        Assert(service.Reads == 1 && ttl.Equals(new Expiration(TimeSpan.FromSeconds(60))), "Dashboard reuses Redis data with a 60-second TTL");
        stored[lastQueryKey] = System.Text.Json.JsonSerializer.Serialize(new[] { engine.Analyze(station with { ReportedAt = DateTime.UtcNow.AddMinutes(-11) }, DateTime.UtcNow.AddMinutes(-11)) });
        Assert((await service.GetDashboardStations())[0].Stale, "Cached reports are rechecked for staleness");
        var oldKey = lastQueryKey;
        config["DataSource:StationPrefix"] = "other:";
        await service.GetDashboardStations();
        Assert(oldKey != lastQueryKey, "Cache key separates providers");
        stored[lastQueryKey] = "broken JSON";
        await service.GetDashboardStations();
        Assert(service.Reads == 3, "Corrupt cache falls back to database loader");
        await service.Invalidate();
        await service.GetDashboardStations();
        Assert(service.Reads == 4, "Mutation invalidates previous list cache generation");
        stored[FleetShared.FleetCacheContract.RevisionKey] = "processor-update";
        await service.GetDashboardStations();
        Assert(service.Reads == 5, "Incoming telemetry invalidates query cache");
        unavailable = true;
        var rows = await service.GetDashboardStations();
        Assert(service.Reads == 6 && rows.Count == 1, "Redis outage falls back and does not fail the dashboard");
        unavailable = false;
        int before = service.Reads;
        await service.GetCachedStation(station.Id); await service.GetCachedStation(station.Id);
        Assert(service.Reads == before + 1 && ttl.Equals(new Expiration(TimeSpan.FromSeconds(60))), "Single station has its own 60-second cache");
        await service.Invalidate();
        await service.GetCachedStation(station.Id); await service.GetDashboardStations();
        Assert(service.Reads == before + 3, "One mutation invalidates both single station and list caches");
    }
    private static void Assert(bool result, string message)
    { if (!result) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private class DataFixture(SmartFleetEngine engine, FleetOptions options, IConfiguration config, IConnectionMultiplexer redis)
        : FleetDataService(null!, null!, engine, options, config, redis, NullLogger<FleetDataService>.Instance, new FleetCacheState())
    {
        public int Reads;
        public List<StationAnalysis> Rows = [];
        public override Task<StationAnalysis?> GetStation(string id, CancellationToken token = default)
        { Reads++; return Task.FromResult(Rows.FirstOrDefault(x => x.Station.Id == id)); }
        public override Task<List<StationAnalysis>> GetStations(CancellationToken token = default)
        { Reads++; return Task.FromResult(Rows); }
    }
}
public class RedisProxy : DispatchProxy
{
    public Func<MethodInfo, object?[], object?> Call = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!, args ?? []);
}
