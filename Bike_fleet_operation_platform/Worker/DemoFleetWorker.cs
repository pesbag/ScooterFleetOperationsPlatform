using System.Text.Json;
using Bike_fleet_operation_platform.Dtos;
using Bike_fleet_operation_platform.Services;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bike_fleet_operation_platform.Workers;

// Clearly labelled synthetic scooter telemetry, sent through the normal Kafka pipeline.
public class DemoFleetWorker(KafkaProducerServices producer, IConfiguration config, ILogger<DemoFleetWorker> logger) : BackgroundService
{
    private static readonly string[] Names = ["Harbor Promenade", "Central Station", "University North", "Market Square",
        "Riverside Park", "City Hall", "Arts District", "East Gardens", "Library Plaza", "West Terminal", "Marina Walk", "Technology Campus"];
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        bool initialized = false;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!initialized)
                {
                    for (int i = 0; i < Names.Length; i++)
                        await Send("StationInformation", new StationInformationDto { Station_id = Id(i), Name = Names[i], Capacity = 40,
                            Lat = 32.07 + i * .003, Lon = 34.77 + (i % 4) * .005 }, Id(i));
                    await Send("VehicleTypes", new VehicleTypesDto { Vehicle_type_id = "demo-scooter", Form_factor = "scooter", propulsion_type = "electric_assist" }, "demo-scooter");
                    // Replay a measured one-hour window so predictions can be demonstrated immediately.
                    var now = DateTimeOffset.UtcNow;
                    for (int minute = -60; minute <= 0; minute += 5)
                        for (int i = 0; i < Names.Length; i++) await Publish(i, now.AddMinutes(minute));
                    initialized = true;
                    logger.LogWarning("DEMO MODE: synthetic scooter data is being published through Kafka.");
                }
                for (int i = 0; i < Names.Length; i++) await Publish(i, DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (!token.IsCancellationRequested) { logger.LogError(ex, "Demo publishing failed; retrying."); initialized = false; }
            await Task.Delay(TimeSpan.FromSeconds(30), token);
        }
    }
    private static string Id(int index) => $"demo-S{index + 1:00}";
    private Task Publish(int index, DateTimeOffset at)
    {
        // A two-hour cycle creates declining, rising and stable stations without negative counts.
        double phase = at.ToUnixTimeSeconds() % 7200 / 7200d;
        int count = index switch { 0 => 3 + (int)(5 * (1 - phase)), 1 => 34 + (int)(5 * phase),
            2 => 8 + (int)(5 * (1 - phase)), 3 => 29 + (int)(5 * phase), 4 => 0, 5 => 40, _ => 17 + index % 5 };
        return Send("StationStatus", new StationStatusDto { Station_id = Id(index), Num_bikes_available = count,
            Nun_vehicles_available = count, Num_docs_available = 40 - count, Is_renting = 1, Is_returning = 1,
            Last_reported = at.ToUnixTimeSeconds() }, Id(index));
    }
    private Task Send<T>(string kind, T value, string id) => producer.SendToKafkaAsync(config[$"Kafka:Topics:{kind}"]!,
        new Message<string, string> { Key = id, Value = JsonSerializer.Serialize(value) });
}
