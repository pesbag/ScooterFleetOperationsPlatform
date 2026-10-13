using Confluent.Kafka;
using System.Text.Json;

namespace BikeFleetOperationPlatform.Services;

public class KafkaProducerServices : IDisposable
{
    private readonly IProducer<string, string> _producer;
    public KafkaProducerServices(string bootstrapServices)
        : this(new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrapServices }).Build())
    {
    }

    internal KafkaProducerServices(IProducer<string, string> producer) => _producer = producer;

    public async Task SendBatchAsync<T>(string topic, IEnumerable<T> items, Func<T, string> keySelector,
        CancellationToken token = default)
    {
        var sends = items.Select(item => SendToKafkaAsync(topic, new Message<string, string>
        {
            Key = keySelector(item),
            Value = JsonSerializer.Serialize(item)
        }, token));
        await Task.WhenAll(sends);
        _producer.Flush(TimeSpan.FromSeconds(10));
    }

    public async Task SendToKafkaAsync(string topicName, Message<string,string> content, CancellationToken token = default)
    {
        try
        {
            var result = await _producer.ProduceAsync(topicName, content, token);
            Console.WriteLine($"delivered station {content.Key} to partition {result.Partition.Value} at offset {result.Offset.Value}");
        }
        catch (ProduceException<string, string> ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"kafka Error for station {content.Key}: {ex.Error.Reason}");
            Console.ResetColor();
            throw;
        }
    }
    public void Dispose() { _producer.Flush(TimeSpan.FromSeconds(5)); _producer.Dispose(); }
}
