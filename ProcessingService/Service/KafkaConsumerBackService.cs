using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProcessingService.Handler;
using System.Text.Json;

public class KafkaConsumerBackgroundService(IServiceScopeFactory scopes, IConfiguration config, ILogger<KafkaConsumerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        await Task.Yield();
        using var initialScope = scopes.CreateScope();
        var topics = initialScope.ServiceProvider.GetServices<IKafkaMessageHandler>().Select(x => x.Topic).Distinct().ToList();
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig {
            BootstrapServers = config["Kafka:BootstrapService"], GroupId = config["Kafka:GroupId"],
            AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false, EnableAutoOffsetStore = false }).Build();
        consumer.Subscribe(topics);
        try
        {
            while (!token.IsCancellationRequested)
            {
                ConsumeResult<string, string>? message = null;
                try
                {
                    message = consumer.Consume(token);
                    using var scope = scopes.CreateScope();
                    var handler = scope.ServiceProvider.GetServices<IKafkaMessageHandler>().First(x => x.Topic == message.Topic);
                    if (!await handler.HandleAsync(message.Message.Value)) throw new JsonException("Invalid metadata.");
                    consumer.Commit(message);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (JsonException ex) { logger.LogError(ex, "Invalid metadata discarded."); if (message is not null) consumer.Commit(message); }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Metadata processing failed; retrying the same message.");
                    if (message is not null) consumer.Seek(message.TopicPartitionOffset);
                    await Task.Delay(2000, token);
                }
            }
        }
        finally { consumer.Close(); }
    }
}
