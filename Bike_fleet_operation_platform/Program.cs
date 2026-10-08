using Bike_fleet_operation_platform.Services;
using Bike_fleet_operation_platform.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

string bootstrapServices = builder.Configuration["Kafka:BootstrapServices"]
    ?? throw new InvalidOperationException("Missing Kafka:BootstrapServices in configuration");

string stationsInformationTopic = builder.Configuration["Kafka:Topics:StationInformation"]
    ?? "bike.station-information";

string stationStatusTopic = builder.Configuration["Kafka:Topics:StationStatus"]
    ?? "bike.station-status";

string vehicleTypeTopic = builder.Configuration["Kafka:Topics:VehicleTypes"]
    ?? "bike.vehicle-types";

string httpClientName = builder.Configuration["StationInformation"]
    ?? "LyftStationClient";

builder.Services.AddHttpClient(
    httpClientName,
    client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["DataSource:BaseUrl"] ?? "https://gbfs.lyft.com/gbfs/2.3/bkn/en/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-docs");
    });

builder.Services.AddSingleton<KafkaProducerServices>(sp => new KafkaProducerServices(bootstrapServices));
builder.Services.AddTransient<StationInformationService>();
builder.Services.AddTransient<StationStatusService>();
builder.Services.AddTransient<VehicleTypesService>();

bool demo = string.Equals(builder.Configuration["DataSource:Mode"], "Demo", StringComparison.OrdinalIgnoreCase);
if (demo) builder.Services.AddHostedService<DemoFleetWorker>();
else builder.Services.AddHostedService<StationsPollingWorker>();

using IHost host = builder.Build();

await host.RunAsync();
