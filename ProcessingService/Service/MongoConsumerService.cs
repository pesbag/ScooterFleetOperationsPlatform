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
                    if (state is null || string.IsNullOrWhiteSpace(state.Station_id) || state.Last_reported <= 0
                        || state.Last_reported >= 253402300800 || state.Num_bikes_available < 0 || state.Num_docs_available < 0)
                        throw new JsonException("Invalid station status.");
                    var at = DateTimeOffset.FromUnixTimeSeconds(state.Last_reported).UtcDateTime;
                    var history = new BsonDocument { ["_id"] = $"{state.Station_id}:{state.Last_reported}",
                        ["StationId"] = state.Station_id, ["RecordedAt"] = at, ["Count"] = state.Num_bikes_available };
                    // Idempotent writes make replays safe when one write succeeds and another fails.
                    await _history.ReplaceOneAsync(Builders<BsonDocument>.Filter.Eq("_id", history["_id"]), history,
                        new ReplaceOptions { IsUpsert = true }, token);
                    var filter = Builders<BsonDocument>.Filter.Eq("Station_id", state.Station_id);
                    var previous = await _latest.Find(filter).FirstOrDefaultAsync(token);
                    if (previous is null || previous.GetValue("Last_reported", 0L).ToInt64() <= state.Last_reported)
                    {
                        if (previous is not null && previous.Contains("_id")) document["_id"] = previous["_id"];
                        await _latest.ReplaceOneAsync(filter, document, new ReplaceOptions { IsUpsert = true }, token);
                        // Cache only after durable writes. Redis failure must not lose telemetry.
                        try { await _redis.StringSetAsync($"stations:state:{state.Station_id}", message.Message.Value, TimeSpan.FromHours(2)); }
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
