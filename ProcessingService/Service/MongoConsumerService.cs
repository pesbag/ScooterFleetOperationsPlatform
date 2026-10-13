using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;
using System.Text.Json;
using ProcessingService.Dtos;

namespace ProcessingService.Service;

public class MongoConsumerService : BackgroundService
{
    private readonly ConsumerConfig _config;
    private readonly IMongoCollection<BsonDocument> _latest;
    private readonly IMongoCollection<BsonDocument> _history;
    private readonly IDatabase _redis;
    private readonly string _topic;
    private readonly ILogger<MongoConsumerService> _logger;
    public MongoConsumerService(ConsumerConfig config, IMongoDatabase database, IConnectionMultiplexer redis,
        IConfiguration configuration, ILogger<MongoConsumerService> logger)
    {
        _config = new ConsumerConfig(config) { GroupId = "stations_status_mongo_group", EnableAutoCommit = false, EnableAutoOffsetStore = false };
        _latest = database.GetCollection<BsonDocument>("StationsStatus");
        _history = database.GetCollection<BsonDocument>("StationHistory");
        _redis = redis.GetDatabase(); _topic = configuration["Kafka:Topics:StationStatus"]!; _logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        await Task.Yield();
        await _history.Indexes.CreateManyAsync([
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("StationId").Ascending("RecordedAt")),
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("RecordedAt"),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(7) })], token);
        using var consumer = new ConsumerBuilder<string, string>(_config).Build();
        consumer.Subscribe(_topic);
        try
        {
            while (!token.IsCancellationRequested)
            {
                ConsumeResult<string, string>? message = null;
                try
                {
                    message = consumer.Consume(token);
                    var document = BsonDocument.Parse(message.Message.Value);
                    if (document.TryGetValue("num_docks_available", out var docks)) { document["Num_docs_available"] = docks; document.Remove("num_docks_available"); }
                    if (document.TryGetValue("num_vehicles_available", out var vehicles)) { document["Nun_vehicles_available"] = vehicles; document.Remove("num_vehicles_available"); }
                    var state = JsonSerializer.Deserialize<StationStatusDto>(document.ToJson(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (state is null || string.IsNullOrWhiteSpace(state.StationId))
                        throw new JsonException("Invalid station status.");
                    // Missing or invalid readings remain visible, but must not become history points.
                    bool validTime = state.LastReported > 0 && state.LastReported < 253402300800
                        && state.LastReported <= DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds();
                    document["ReceivedAt"] = state.ReceivedAt is { } received ? new BsonDateTime(received) : BsonNull.Value;
                    if (validTime && state.NumBikesAvailable is >= 0)
                    {
                        var at = DateTimeOffset.FromUnixTimeSeconds(state.LastReported!.Value).UtcDateTime;
                        var history = new BsonDocument { ["_id"] = $"{state.StationId}:{state.LastReported}",
                            ["StationId"] = state.StationId, ["RecordedAt"] = at, ["Count"] = state.NumBikesAvailable.Value };
                        // Idempotent writes make replays safe when one write succeeds and another fails.
                        await _history.ReplaceOneAsync(Builders<BsonDocument>.Filter.Eq("_id", history["_id"]), history,
                            new ReplaceOptions { IsUpsert = true }, token);
                    }
                    var filter = Builders<BsonDocument>.Filter.Eq("Station_id", state.StationId);
                    var previous = await _latest.Find(filter).FirstOrDefaultAsync(token);
                    var previousTime = previous?.GetValue("Last_reported", BsonNull.Value) ?? BsonNull.Value;
                    long previousTimestamp = previousTime.IsInt32 || previousTime.IsInt64 ? previousTime.ToInt64() : 0;
                    if (previous is null || !validTime || previousTimestamp <= state.LastReported
                        || previousTimestamp > DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds())
                    {
                        if (previous is not null && previous.Contains("_id")) document["_id"] = previous["_id"];
                        await _latest.ReplaceOneAsync(filter, document, new ReplaceOptions { IsUpsert = true }, token);
                        // Cache only after durable writes. Redis failure must not lose telemetry.
                        try { await _redis.StringSetAsync(FleetShared.FleetCacheContract.RevisionKey, Guid.NewGuid().ToString("N")); }
                        catch (RedisException ex) { _logger.LogWarning(ex, "Redis cache unavailable."); }
                    }
                    consumer.Commit(message);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid message discarded at {Offset}.", message?.TopicPartitionOffset);
                    if (message is not null) consumer.Commit(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Status processing failed; retrying the same message.");
                    if (message is not null) consumer.Seek(message.TopicPartitionOffset);
                    await Task.Delay(2000, token);
                }
            }
        }
        finally { consumer.Close(); }
    }
}
