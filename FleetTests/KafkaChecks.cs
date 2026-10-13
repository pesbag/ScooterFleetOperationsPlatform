using System.Reflection;
using System.Text.Json;
using BikeFleetOperationPlatform.Services;
using BikeFleetOperationPlatform.Dtos;
using Confluent.Kafka;

public static class KafkaChecks
{
    public static async Task Run()
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var incoming = JsonSerializer.Deserialize<StationStatusDto>("{\"station_id\":\"one\",\"num_bikes_available\":7,\"num_docks_available\":3,\"is_renting\":true,\"last_reported\":1791807000}", jsonOptions)!;
        Check(incoming.StationId == "one" && incoming.NumBikesAvailable == 7 && incoming.NumDocksAvailable == 3 && incoming.IsRenting == 1,
            "PascalCase properties preserve GBFS field mapping");
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(incoming));
        Check(wire.RootElement.GetProperty("Station_id").GetString() == "one" && wire.RootElement.GetProperty("num_docks_available").GetInt32() == 3,
            "PascalCase rename preserves Kafka JSON contract");
        var stored = JsonSerializer.Deserialize<ProcessingService.Dtos.StationStatusDto>("{\"Station_id\":\"one\",\"Num_bikes_available\":7,\"Num_docs_available\":3,\"Nun_vehicles_available\":1}", jsonOptions)!;
        Check(stored.StationId == "one" && stored.NumDocksAvailable == 3 && stored.NumVehiclesAvailable == 1,
            "PascalCase properties read existing Mongo field names");
        using var legacy = JsonDocument.Parse(JsonSerializer.Serialize(new StationApi.Dtos.AllDataDto { StationId = "one", NumDocksAvailable = 3 }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Check(legacy.RootElement.GetProperty("station_id").GetString() == "one" && legacy.RootElement.GetProperty("num_docs_available").GetInt32() == 3,
            "Legacy API keeps its existing response field names");
        Check(typeof(StationApi.Dtos.StationInformationDto).GetProperty("StationId")!.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>()?.Name == "Station_id",
            "Renamed station property retains SQL column name");
        var producer = DispatchProxy.Create<IProducer<string, string>, ProducerStub>();
        var stub = (ProducerStub)(object)producer;
        using var service = new KafkaProducerServices(producer);
        using var cancellation = new CancellationTokenSource();
        var items = new[] {
            new StationStatusDto { StationId = "citibike:1", NumBikesAvailable = null },
            new StationStatusDto { StationId = "citibike:2", NumBikesAvailable = 0 }
        };

        var sending = service.SendBatchAsync("status", items, item => item.StationId, cancellation.Token);
        Check(stub.Messages.Count == 2 && !sending.IsCompleted && stub.Flushes == 0,
            "Kafka batch waits for every delivery before flushing");
        Check(stub.Messages.Select(x => x.Key).SequenceEqual(items.Select(x => x.StationId))
            && stub.Topics.All(x => x == "status") && stub.Tokens.All(x => x == cancellation.Token),
            "Kafka batch preserves topic, keys and cancellation token");
        Check(stub.Messages.Select(x => x.Value).SequenceEqual(items.Select(item => JsonSerializer.Serialize(item))),
            "Kafka batch preserves existing JSON including missing and zero counts");
        foreach (var pending in stub.Pending) pending.SetResult(new DeliveryResult<string, string>());
        await sending;
        Check(stub.Flushes == 1 && stub.LastTimeout == TimeSpan.FromSeconds(10), "Kafka batch flushes after successful delivery");

        var failed = service.SendBatchAsync("status", items.Take(1), item => item.StationId);
        stub.Pending[^1].SetException(new InvalidOperationException("Simulated delivery failure"));
        try { await failed; throw new Exception("Delivery failure was swallowed"); }
        catch (InvalidOperationException) { Check(stub.Flushes == 1, "Kafka delivery failure propagates without a success flush"); }

        var canceled = service.SendBatchAsync("status", items.Take(1), item => item.StationId, cancellation.Token);
        cancellation.Cancel();
        stub.Pending[^1].SetCanceled(cancellation.Token);
        try { await canceled; throw new Exception("Cancellation was swallowed"); }
        catch (OperationCanceledException) { Check(stub.Flushes == 1, "Kafka cancellation propagates"); }

        int count = stub.Messages.Count;
        await service.SendBatchAsync("status", Array.Empty<StationStatusDto>(), item => item.StationId);
        Check(stub.Messages.Count == count && stub.Flushes == 2, "Empty Kafka batch publishes no fabricated records");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    public class ProducerStub : DispatchProxy
    {
        public List<Message<string, string>> Messages { get; } = [];
        public List<string> Topics { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public List<TaskCompletionSource<DeliveryResult<string, string>>> Pending { get; } = [];
        public int Flushes { get; private set; }
        public TimeSpan LastTimeout { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "ProduceAsync":
                    Topics.Add((string)args![0]!);
                    Messages.Add((Message<string, string>)args[1]!);
                    Tokens.Add((CancellationToken)args[2]!);
                    var pending = new TaskCompletionSource<DeliveryResult<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Pending.Add(pending);
                    return pending.Task;
                case "Flush":
                    Flushes++;
                    LastTimeout = (TimeSpan)args![0]!;
                    return 0;
                case "Dispose": return null;
                default: throw new NotSupportedException(method.Name);
            }
        }
    }
}
