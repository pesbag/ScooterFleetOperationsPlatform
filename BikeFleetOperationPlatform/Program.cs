using BikeFleetOperationPlatform.Services;
using BikeFleetOperationPlatform.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

if (!Uri.TryCreate(builder.Configuration["DataSource:BaseUrl"], UriKind.Absolute, out var sourceUri)
    || sourceUri.Scheme != "https" || !sourceUri.AbsolutePath.EndsWith('/'))
    throw new InvalidOperationException("DataSource:BaseUrl must be an HTTPS GBFS directory URL ending in '/'.");
if (string.IsNullOrWhiteSpace(builder.Configuration["DataSource:StationPrefix"]))
    throw new InvalidOperationException("Set DataSource:StationPrefix to a unique source prefix, for example citibike:.");
if (builder.Configuration.GetValue<int>("Http:TimeoutSeconds") is < 1 or > 120)
    throw new InvalidOperationException("Http:TimeoutSeconds must be between 1 and 120.");
foreach (var key in new[] { "StationInformationIntervalSeconds", "StationStatusIntervalSeconds", "VehicleTypesIntervalSeconds" })
    if (builder.Configuration.GetValue<int>("Polling:" + key) < 30) throw new InvalidOperationException("Polling:" + key + " must be at least 30 seconds.");
string bootstrapServices = builder.Configuration["Kafka:BootstrapServices"]
    ?? throw new InvalidOperationException("Missing Kafka:BootstrapServices in configuration");

string httpClientName = builder.Configuration["StationInformation"]
    ?? "LyftStationClient";

builder.Services.AddHttpClient(
    httpClientName,
    client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["DataSource:BaseUrl"]!);
        client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("Http:TimeoutSeconds"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BikeFleet/1.0");
    });

builder.Services.AddSingleton<KafkaProducerServices>(sp => new KafkaProducerServices(bootstrapServices));
builder.Services.AddTransient<StationInformationService>();
builder.Services.AddTransient<StationStatusService>();
builder.Services.AddTransient<VehicleTypesService>();

builder.Services.AddHostedService<StationsPollingWorker>();

using IHost host = builder.Build();

await host.RunAsync();
