using Confluent.Kafka;

namespace Bike_fleet_operation_platform.Services;

public class KafkaProducerServices : IDisposable
{
    private readonly string _bootstrapServices;
    private readonly IProducer<string, string> _producer;
    public KafkaProducerServices(string bootstrapServices)
    {
        _bootstrapServices = bootstrapServices;
        var config = new ProducerConfig { BootstrapServers = bootstrapServices };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public void Flush(TimeSpan timeout)
    {
        _producer.Flush(timeout);
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
