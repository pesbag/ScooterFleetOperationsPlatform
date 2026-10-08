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
