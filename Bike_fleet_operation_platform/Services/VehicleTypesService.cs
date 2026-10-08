using Bike_fleet_operation_platform.Dtos;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bike_fleet_operation_platform.Services;

public class VehicleTypesService
{
    private readonly IHttpClientFactory _httpClientFactory = null!;
    private readonly IConfiguration _configuration = null!;
    private readonly ILogger<VehicleTypesService> _logger = null!;
    private readonly KafkaProducerServices _kafkaProducer;

    public VehicleTypesService(IHttpClientFactory httpClientFactory,
                                IConfiguration configuration,
                                ILogger<VehicleTypesService> logger,
                                KafkaProducerServices kafkaProducer) =>
        (_httpClientFactory, _configuration, _logger,_kafkaProducer) =
        (httpClientFactory, configuration, logger,kafkaProducer);


    public async Task<VehicleTypesDto[]> GetVehicleTypesDtoAsync(string topic)
    {
        string? httpClientName = _configuration["StationInformation"] ?? "LyftStationClient";
        HttpClient client = _httpClientFactory.CreateClient(httpClientName);
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using JsonDocument document = await client.GetFromJsonAsync<JsonDocument>("vehicle_types.json")
                ?? throw new InvalidOperationException("failed to read json document");

            JsonElement stationsElement = document.RootElement
             .GetProperty("data")
             .GetProperty("vehicle_types");

            VehicleTypesDto[]? vehicleTypes = stationsElement.Deserialize<VehicleTypesDto[]>(options);
            if (vehicleTypes is null)
            {
                _logger.LogError("\"error in deserialize vehicle types\"");
                Console.WriteLine("\"error in deserialize vehicle types\"");
            }
            Console.WriteLine($"succesfully recived {(vehicleTypes?.Length ?? 0)} vehicle types information");
            var sendTasks = (vehicleTypes ?? []).Select(content =>
            {
                var message = new Message<string, string>
                {
                    Key = content.Vehicle_type_id,
                    Value = JsonSerializer.Serialize(content)
                };

                return _kafkaProducer.SendToKafkaAsync(topic, message);
            });
            await Task.WhenAll(sendTasks);
            _kafkaProducer.Flush(TimeSpan.FromSeconds(10));
            return vehicleTypes ?? [];
        }
        catch(Exception ex)
        {
            Console.WriteLine($"the error: {ex.Message}");
            _logger.LogError("error getting something fun to say: {Error}", ex);
        }
        return [];
    }
}
