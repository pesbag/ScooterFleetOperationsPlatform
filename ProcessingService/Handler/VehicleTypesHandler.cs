using ProcessingService.DataDbContext;
using ProcessingService.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
        var existing = await _context.vehicleTypes.FindAsync(dto.Vehicle_type_id);
        if (existing is null)
            await _context.vehicleTypes.AddAsync(dto);
        else
            _context.Entry(existing).CurrentValues.SetValues(dto);
        await _context.SaveChangesAsync();
        return true;
    }
}
