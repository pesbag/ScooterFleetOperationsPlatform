using Microsoft.EntityFrameworkCore;
using StationApi.Models;

namespace StationApi.DataDbContext;

// Separate database ownership keeps the existing ingestion schema untouched.
public class OperationsDbContext(DbContextOptions<OperationsDbContext> options) : DbContext(options)
{
    public DbSet<FleetUser> Users => Set<FleetUser>();
    public DbSet<FleetAlert> Alerts => Set<FleetAlert>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<RiderFavorite> RiderFavorites => Set<RiderFavorite>();
    public DbSet<RiderIssue> RiderIssues => Set<RiderIssue>();
    public DbSet<TaskMeasurement> TaskMeasurements => Set<TaskMeasurement>();
    public DbSet<ForecastSample> ForecastSamples => Set<ForecastSample>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<RiderFavorite>().HasKey(x => new { x.UserId, x.StationId });
        model.Entity<RiderIssue>().HasIndex(x => new { x.StationId, x.Status });
        model.Entity<TaskMeasurement>().HasKey(x => x.RecommendationId);
        model.Entity<TaskMeasurement>().Property(x => x.RecommendationId).ValueGeneratedNever();
        model.Entity<ForecastSample>().HasIndex(x => new { x.StationId, x.IssuedAt });
        model.Entity<FleetUser>().HasIndex(x => x.Email).IsUnique();
        model.Entity<FleetAlert>().HasIndex(x => new { x.StationId, x.Status });
        model.Entity<Recommendation>().HasIndex(x => x.Status);
    }
}
