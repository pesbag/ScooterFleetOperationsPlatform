using Microsoft.EntityFrameworkCore;
using StationApi.Models;

namespace StationApi.DataDbContext;

// Separate database ownership keeps the existing ingestion schema untouched.
public class OperationsDbContext(DbContextOptions<OperationsDbContext> options) : DbContext(options)
{
    public DbSet<FleetUser> Users => Set<FleetUser>();
    public DbSet<FleetAlert> Alerts => Set<FleetAlert>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<FleetUser>().HasIndex(x => x.Email).IsUnique();
        model.Entity<FleetAlert>().HasIndex(x => new { x.StationId, x.Status });
        model.Entity<Recommendation>().HasIndex(x => x.Status);
    }
}
