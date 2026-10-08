using Bike_fleet_operation_platform.Dtos;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bike_fleet_operation_platform.Services;

public class StationStatusService
{
    private readonly IHttpClientFactory _httpClientFactory = null!;
    private readonly IConfiguration _configuration = null!;
    private readonly ILogger<StationStatusService> _logger = null!;
    private readonly KafkaProducerServices _kafkaProducer;

    public StationStatusService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<StationStatusService> logger,
        KafkaProducerServices kafkaProducer) =>
        (_httpClientFactory, _configuration, _logger, _kafkaProducer) =
            (httpClientFactory, configuration, logger, kafkaProducer);

    public async Task<StationStatusDto[]> GetStationsStatusAsync(string topic)
    {
        // create the client
        string? httpClientName = _configuration["StationInformation"] ?? "LyftStationClient";
        HttpClient client = _httpClientFactory.CreateClient(httpClientName);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using JsonDocument document = await client.GetFromJsonAsync<JsonDocument>("station_status.json")
                ?? throw new InvalidOperationException("failed to read json document");

            JsonElement stationsElement = document.RootElement
            .GetProperty("data")
            .GetProperty("stations");

            StationStatusDto[]? stationsStatus = stationsElement.Deserialize<StationStatusDto[]>(options);
            if(stationsStatus is null)
            {
                _logger.LogError("error in deserialize stations status");
                Console.WriteLine("error in deserialize stations status");
            }
            Console.WriteLine($"succesfully recived {(stationsStatus?.Length ?? 0)} stations status");
            var validStations = new List<StationStatusDto>();
            foreach(var station in stationsStatus ?? [])
            {
                var validationContext = new ValidationContext(station);
                var validationResults = new List<ValidationResult>();
                
                bool isValid = Validator.TryValidateObject(station, validationContext, validationResults, validateAllProperties: true);

                if (isValid)
                {
                    validStations.Add(station);
                }
                else
                {
                    var errors = string.Join(", ", validationResults.Select(r => r.ErrorMessage));
                    _logger.LogWarning("Station {Id} failed validation: {Errors}", station.Station_id, errors);
                }
            }
            var sendTasks = validStations.Select(content =>
            {
                var message = new Message<string, string>
                {
                    Key = content.Station_id,
                    Value = JsonSerializer.Serialize(content)
                };

                return _kafkaProducer.SendToKafkaAsync(topic, message);
            });
            await Task.WhenAll(sendTasks);
            _kafkaProducer.Flush(TimeSpan.FromSeconds(10));
            return stationsStatus ?? [];

        }
        catch (Exception ex)
        {
            Console.WriteLine($"the error: {ex.Message}");
            _logger.LogError("Error getting something fun to say: {Error}", ex);
        }
        return [];
    }
}
