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

    public async Task<StationStatusDto[]> GetStationsStatusAsync(string topic, CancellationToken token = default)
    {
        // create the client
        string? httpClientName = _configuration["StationInformation"] ?? "LyftStationClient";
        HttpClient client = _httpClientFactory.CreateClient(httpClientName);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using JsonDocument document = await GbfsHttp.GetJson(client, "station_status.json", _configuration.GetValue<int>("Http:MaxAttempts", 3), token);

            var receivedAt = DateTime.UtcNow;
            StationStatusDto[] stationsStatus = GbfsJson.Stations(document.RootElement, null)
                .Select(x => x.Deserialize<StationStatusDto>(options)!).ToArray();
            if(stationsStatus is null)
            {
                _logger.LogError("error in deserialize stations status");
                Console.WriteLine("error in deserialize stations status");
            }
            Console.WriteLine($"succesfully recived {(stationsStatus?.Length ?? 0)} stations status");
            var validStations = new List<StationStatusDto>();
            foreach(var station in stationsStatus ?? [])
            {
                station.ReceivedAt = receivedAt;
                if (string.IsNullOrWhiteSpace(station.Station_id)) { _logger.LogWarning("GBFS station without an ID was skipped."); continue; }
                station.Station_id = _configuration["DataSource:StationPrefix"] + station.Station_id;
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

                return _kafkaProducer.SendToKafkaAsync(topic, message, token);
            });
            await Task.WhenAll(sendTasks);
            _kafkaProducer.Flush(TimeSpan.FromSeconds(10));
            return stationsStatus ?? [];

        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Console.WriteLine($"the error: {ex.Message}");
            _logger.LogError("GBFS station status request failed: {Error}", ex);
        }
        throw new HttpRequestException("GBFS request failed. Existing source timestamps were left unchanged.");
    }
}
