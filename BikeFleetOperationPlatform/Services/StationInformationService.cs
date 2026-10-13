using BikeFleetOperationPlatform.Dtos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace BikeFleetOperationPlatform.Services;

public class StationInformationService
{
    private readonly KafkaProducerServices _kafkaProducer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StationInformationService> _logger;

    public StationInformationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<StationInformationService> logger,
        KafkaProducerServices kafkaProducer) =>
        (_httpClientFactory, _configuration, _logger, _kafkaProducer) =
            (httpClientFactory, configuration, logger, kafkaProducer);

    public async Task<StationInformationDto[]> GetStationsInformationAsync(string topic, CancellationToken token = default)
    {
        string? httpClientName = _configuration["StationInformation"] ?? "LyftStationClient";
        HttpClient client = _httpClientFactory.CreateClient(httpClientName);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using JsonDocument document = await GbfsHttp.GetJsonAsync(client, "station_information.json", _configuration.GetValue<int>("Http:MaxAttempts", 3), token);

            StationInformationDto[] stationsInformation = document.RootElement.GetProperty("data").GetProperty("stations").EnumerateArray()
                .Select(x => x.Deserialize<StationInformationDto>(options)!).ToArray();

            var validStations = new List<StationInformationDto>();

            foreach (var station in stationsInformation)
            {
                if (string.IsNullOrWhiteSpace(station.StationId)) { _logger.LogWarning("GBFS station without an ID was skipped."); continue; }
                station.StationId = _configuration["DataSource:StationPrefix"] + station.StationId;
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
                    _logger.LogWarning("station {Id} failed validation: {errors}", station.StationId, errors);
                }
            }
            await _kafkaProducer.SendBatchAsync(topic, validStations, item => item.StationId, token);
            return stationsInformation;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError("GBFS station information request failed: {Error}", ex);
        }
        throw new HttpRequestException("GBFS request failed. Existing source timestamps were left unchanged.");
    }
}
