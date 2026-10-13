using ProcessingService.Dtos;
using Microsoft.EntityFrameworkCore;
namespace ProcessingService.DataDbContext;

public class StationDbContext: DbContext
{
    public StationDbContext(DbContextOptions<StationDbContext> options):base(options) {}
    public DbSet<StationInformationDto> StationInformation { get; set; }
    public DbSet<VehicleTypesDto> VehicleTypes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<StationInformationDto>()
            .ToTable("stationInformation").HasKey(e => e.StationId);
        modelBuilder.Entity<VehicleTypesDto>()
            .ToTable("vehicleTypes").HasKey(e => e.VehicleTypeId);

    }
}