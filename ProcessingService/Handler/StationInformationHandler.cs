using ProcessingService.DataDbContext;
using ProcessingService.Dtos;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProcessingService.Handler;


public class StationInformationHandler : IKafkaMessageHandler
{
    private readonly StationDbContext _context;
    private readonly IDatabase _redisDb;
    private readonly Microsoft.Extensions.Logging.ILogger<StationInformationHandler> _logger;
    public string Topic { get; }

    public StationInformationHandler(StationDbContext context, IDatabase redisDb, Microsoft.Extensions.Configuration.IConfiguration config, Microsoft.Extensions.Logging.ILogger<StationInformationHandler> logger)
    {
        Topic = config["Kafka:Topics:StationInformation"] ?? throw new InvalidOperationException("Missing station information topic.");
        _logger = logger;
        _context = context;
        _redisDb = redisDb;
    }

    public async Task<bool> HandleAsync(string jsonMessage)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dto = JsonSerializer.Deserialize<StationInformationDto>(jsonMessage, options);
        if (dto is null) return false;

        var existing = await _context.stationInformation.FindAsync(dto.Station_id);
        if (existing is null)
            await _context.stationInformation.AddAsync(dto);
        else
            _context.Entry(existing).CurrentValues.SetValues(dto);

        await _context.SaveChangesAsync();

        try { await _redisDb.StringSetAsync(FleetShared.FleetCacheContract.RevisionKey, Guid.NewGuid().ToString("N")); }
        catch (RedisException ex) { Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger, ex, "Metadata saved; Redis invalidation unavailable."); }

        return true;
    }
}