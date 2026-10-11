using Bike_fleet_operation_platform.Dtos;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Bike_fleet_operation_platform.Services;

public class VehicleTypesService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VehicleTypesService> _logger;
    private readonly KafkaProducerServices _kafkaProducer;

    public VehicleTypesService(IHttpClientFactory httpClientFactory,
                                IConfiguration configuration,
                                ILogger<VehicleTypesService> logger,
                                KafkaProducerServices kafkaProducer) =>
        (_httpClientFactory, _configuration, _logger,_kafkaProducer) =
        (httpClientFactory, configuration, logger,kafkaProducer);


    public async Task<VehicleTypesDto[]> GetVehicleTypesDtoAsync(string topic, CancellationToken token = default)
    {
        string? httpClientName = _configuration["StationInformation"] ?? "LyftStationClient";
        HttpClient client = _httpClientFactory.CreateClient(httpClientName);
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using JsonDocument document = await GbfsHttp.GetJsonAsync(client, "vehicle_types.json", _configuration.GetValue<int>("Http:MaxAttempts", 3), token);

            JsonElement stationsElement = document.RootElement
             .GetProperty("data")
             .GetProperty("vehicle_types");

            var vehicleTypes = stationsElement.Deserialize<VehicleTypesDto[]>(options)
                ?? throw new JsonException("GBFS vehicle_types must be an array.");
            var sendTasks = vehicleTypes.Select(content =>
            {
                var message = new Message<string, string>
                {
                    Key = content.Vehicle_type_id,
                    Value = JsonSerializer.Serialize(content)
                };

                return _kafkaProducer.SendToKafkaAsync(topic, message, token);
            });
            await Task.WhenAll(sendTasks);
            _kafkaProducer.Flush(TimeSpan.FromSeconds(10));
            return vehicleTypes;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch(Exception ex)
        {
            _logger.LogError("GBFS vehicle types request failed: {Error}", ex);
        }
        throw new HttpRequestException("GBFS request failed. Existing source timestamps were left unchanged.");
    }
}
