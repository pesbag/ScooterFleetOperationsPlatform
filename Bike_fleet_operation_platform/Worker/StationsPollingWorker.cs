using Bike_fleet_operation_platform.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bike_fleet_operation_platform.Workers;

public class StationsPollingWorker : BackgroundService
{
    private readonly StationInformationService _infoService;
    private readonly StationStatusService _statusService;
    private readonly VehicleTypesService _vehicleTypesService;
    private readonly IConfiguration _config;
    private readonly ILogger<StationsPollingWorker> _logger;

    public StationsPollingWorker(
        StationInformationService infoService,
        StationStatusService statusService,
        VehicleTypesService vehicleTypesService,
        IConfiguration config,
        ILogger<StationsPollingWorker> logger)
    {
        _vehicleTypesService = vehicleTypesService;
        _infoService = infoService;
        _statusService = statusService;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string infoTopic = _config["Kafka:Topics:StationInformation"]!;
        int infoSeconds = _config.GetValue<int>("Polling:StationInformationIntervalSeconds", 3600);

        string vehicleTopic = _config["Kafka:Topics:VehicleTypes"]!;
        int vehicleSeconds = _config.GetValue<int>("Polling:VehicleTypesIntervalSeconds", 3600);

        string statusTopic = _config["Kafka:Topics:StationStatus"]!;
        int statusSeconds = _config.GetValue<int>("Polling:StationStatusIntervalSeconds", 30);

        var vehicleTask = RunPeriodicJobAsync(
            jobName: "VehicleTypes",
            interval: TimeSpan.FromSeconds(vehicleSeconds),
            action: () => _vehicleTypesService.GetVehicleTypesDtoAsync(vehicleTopic, stoppingToken),
            stoppingToken);

        var infoTask = RunPeriodicJobAsync(
            jobName: "StationInformation",
            interval: TimeSpan.FromSeconds(infoSeconds),
            action: () => _infoService.GetStationsInformationAsync(infoTopic, stoppingToken),
            stoppingToken);

        var statusTask = RunPeriodicJobAsync(
            jobName: "StationStatus",
            interval: TimeSpan.FromSeconds(statusSeconds),
            action: () => _statusService.GetStationsStatusAsync(statusTopic, stoppingToken),
            stoppingToken);

        await Task.WhenAll(infoTask, statusTask, vehicleTask);
    }

    private async Task RunPeriodicJobAsync(
        string jobName,
        TimeSpan interval,
        Func<Task> action,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job {JobName} started (every {Interval}s)", jobName, interval.TotalSeconds);
        using var timer = new PeriodicTimer(interval);
        try
        {
            await ExecuteSafelyAsync(jobName, action);

            while (!stoppingToken.IsCancellationRequested &&
                   await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ExecuteSafelyAsync(jobName, action);
            }
        }
        catch(OperationCanceledException)
        {
            _logger.LogInformation("Job {JobName} gracefully stopped.", jobName);
        }
    }

    private async Task ExecuteSafelyAsync(string jobName, Func<Task> action)
    {
        try
        {
            _logger.LogDebug("Executing {JobName}...", jobName);
            await action();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while running job {JobName}", jobName);
        }
    }
}