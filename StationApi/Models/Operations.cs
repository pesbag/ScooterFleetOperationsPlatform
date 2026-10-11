using System.ComponentModel.DataAnnotations;

namespace StationApi.Models;

public class FleetUser
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(160)] public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    [MaxLength(16)] public string Role { get; set; } = "OPERATOR";
    public bool Enabled { get; set; } = true;
    public int SessionVersion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class FleetAlert
{
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string? StationName { get; set; }
    public long Id { get; set; }
    [MaxLength(255)] public string StationId { get; set; } = "";
    public string Severity { get; set; } = "WARNING";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "ACTIVE";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedBy { get; set; }
}

public class Recommendation
{
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string? SourceStationName { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string? DestinationStationName { get; set; }
    public long Id { get; set; }
    [MaxLength(255)] public string SourceStationId { get; set; } = "";
    [MaxLength(255)] public string DestinationStationId { get; set; } = "";
    public int Quantity { get; set; }
    public string Priority { get; set; } = "HIGH";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public class RiderFavorite
{
    public int UserId { get; set; }
    [MaxLength(255)] public string StationId { get; set; } = "";
}
public class RiderIssue
{
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string? StationName { get; set; }
    public long Id { get; set; }
    public int UserId { get; set; }
    [MaxLength(255)] public string StationId { get; set; } = "";
    [MaxLength(1000)] public string Description { get; set; } = "";
    [MaxLength(16)] public string Status { get; set; } = "OPEN";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedBy { get; set; }
}
public class TaskMeasurement
{
    public long RecommendationId { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DestinationBefore { get; set; }
    public int? DestinationAfter { get; set; }
    public DateTime? ObservedAt { get; set; }
}
public class ForecastSample
{
    public long Id { get; set; }
    [MaxLength(255)] public string StationId { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public DateTime DueAt { get; set; }
    public double PredictedCount { get; set; }
    public int? ActualCount { get; set; }
    public DateTime? ObservedAt { get; set; }
}
