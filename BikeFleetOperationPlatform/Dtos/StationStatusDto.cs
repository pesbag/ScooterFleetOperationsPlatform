using System.ComponentModel.DataAnnotations;

namespace BikeFleetOperationPlatform.Dtos;

public class StationStatusDto
{
    [Required]
    [System.Text.Json.Serialization.JsonPropertyName("Station_id")]
    public string StationId { get; set; } = string.Empty;
    [Range(0,int.MaxValue)]
    [System.Text.Json.Serialization.JsonPropertyName("Num_bikes_available")]
    public int? NumBikesAvailable { get; set; }
    [Range(0, int.MaxValue)]
    [System.Text.Json.Serialization.JsonPropertyName("num_vehicles_available")]
    public int? NumVehiclesAvailable { get; set; }
    [Range(0, int.MaxValue)]
    [System.Text.Json.Serialization.JsonPropertyName("num_docks_available")]
    public int? NumDocksAvailable { get; set; }
    [System.Text.Json.Serialization.JsonConverter(typeof(BikeFleetOperationPlatform.Services.GbfsFlagConverter))]
    [System.Text.Json.Serialization.JsonPropertyName("Is_renting")]
    public int? IsRenting { get; set; }
    [System.Text.Json.Serialization.JsonConverter(typeof(BikeFleetOperationPlatform.Services.GbfsFlagConverter))]
    [System.Text.Json.Serialization.JsonPropertyName("Is_returning")]
    public int? IsReturning { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Last_reported")]
    public long? LastReported { get; set; }
    public DateTime? ReceivedAt { get; set; }
}
