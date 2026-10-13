using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using ProcessingService.DataDbContext;
using ProcessingService.Handler;
using ProcessingService.Service;
using StackExchange.Redis;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection in appsettings.json");

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    string redisConn = configuration["Redis:ConnectionString"]!;
    return ConnectionMultiplexer.Connect(redisConn);
});

builder.Services.AddSingleton<IDatabase>(sp =>
{
    var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
    return multiplexer.GetDatabase();
});

builder.Services.AddDbContext<StationDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))));


builder.Services.AddHostedService<KafkaConsumerBackgroundService>();
builder.Services.AddScoped<IKafkaMessageHandler, StationInformationHandler>();
builder.Services.AddScoped<IKafkaMessageHandler, VehicleTypesHandler>();

var consumerConfig = new ConsumerConfig();
builder.Configuration.GetSection("Kafka:Consumer").Bind(consumerConfig);
builder.Services.AddSingleton(consumerConfig);

builder.Services.AddSingleton<IMongoClient>(sp =>
        new MongoClient(builder.Configuration["MongoDb:ConnectionString"]));

    builder.Services.AddSingleton<IMongoDatabase>(sp =>
    {
        var client = sp.GetRequiredService<IMongoClient>();
        return client.GetDatabase(builder.Configuration["MongoDb:DatabaseName"]);
    });

    builder.Services.AddHostedService<MongoConsumerService>();


using IHost host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<StationDbContext>();
    dbContext.Database.EnsureCreated();
}

    await host.RunAsync();