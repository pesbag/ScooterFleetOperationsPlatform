using ProcessingService.DataDbContext;
using ProcessingService.Dtos;
using System.Text.Json;

namespace ProcessingService.Handler;

public class VehicleTypesHandler:IKafkaMessageHandler
{
    private readonly StationDbContext _context;
    public string Topic { get; }
    public VehicleTypesHandler(StationDbContext context, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        Topic = config["Kafka:Topics:VehicleTypes"] ?? throw new InvalidOperationException("Missing vehicle types topic.");
        _context = context;
    }
    public async Task<bool> HandleAsync(string jsonMessage)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dto = JsonSerializer.Deserialize<VehicleTypesDto>(jsonMessage,options);
        if (dto is null) { return false; }
        var existing = await _context.VehicleTypes.FindAsync(dto.VehicleTypeId);
        if (existing is null)
            await _context.VehicleTypes.AddAsync(dto);
        else
            _context.Entry(existing).CurrentValues.SetValues(dto);
        await _context.SaveChangesAsync();
        return true;
    }
}
