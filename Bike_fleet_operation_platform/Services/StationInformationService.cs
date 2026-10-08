using Bike_fleet_operation_platform.Dtos;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static Confluent.Kafka.ConfigPropertyNames;

namespace Bike_fleet_operation_platform.Services;

public class StationInformationService
{
    private readonly KafkaProducerServices _kafkaProducer;
    private readonly IHttpClientFactory _httpClientFactory = null!;
    private readonly IConfiguration _configuration = null!;
    private readonly ILogger<StationInformationService> _logger = null!;

    public StationInformationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<StationInformationService> logger,
        KafkaProducerServices kafkaProducer) =>
        (_httpClientFactory, _configuration, _logger, _kafkaProducer) =
            (httpClientFactory, configuration, logger, kafkaProducer);

    public async Task<StationInformationDto[]> GetStationsInformationAsync(string topic)
    {
        // create the client
        string? httpClientName = _configuration["StationInformation"] ?? "LyftStationClient";
        HttpClient client = _httpClientFactory.CreateClient(httpClientName);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using JsonDocument document = await client.GetFromJsonAsync<JsonDocument>("station_information.json")
                ?? throw new InvalidOperationException("failed to read json document");

            JsonElement stationsElement = document.RootElement
            .GetProperty("data")
            .GetProperty("stations");

            StationInformationDto[]? stationsInformation = stationsElement.Deserialize<StationInformationDto[]>(options);
            if (stationsInformation is null)
            {
                _logger.LogError("\"error in deserialize stations information\"");
                Console.WriteLine("\"error in deserialize stations information\"");
            }
            Console.WriteLine($"succesfully recived {(stationsInformation?.Length ?? 0)} stations information");

            var validStations = new List<StationInformationDto>();

            foreach (var station in stationsInformation ?? [])
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
                    _logger.LogWarning("station {Id} failed validation: {errors}", station.Station_id, errors);
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
            return stationsInformation ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"the error: {ex.Message}");
            _logger.LogError("Error getting something fun to say: {Error}", ex);
        }
        return [];
    }
}
