using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;
namespace ProcessingService.Dtos;

public class StationStatusDto
{
    [Required]
    [BsonId]
    [System.Text.Json.Serialization.JsonPropertyName("Station_id")]
    public string StationId { get; set; } = string.Empty;
    [Range(0,int.MaxValue)]
    [System.Text.Json.Serialization.JsonPropertyName("Num_bikes_available")]
    public int? NumBikesAvailable { get; set; }
    [Range(0, int.MaxValue)]
    [System.Text.Json.Serialization.JsonPropertyName("Nun_vehicles_available")]
    public int? NumVehiclesAvailable { get; set; }
    [Range(0, int.MaxValue)]
    [System.Text.Json.Serialization.JsonPropertyName("Num_docs_available")]
    public int? NumDocksAvailable { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Is_renting")]
    public int? IsRenting { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Is_returning")]
    public int? IsReturning { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Last_reported")]
    public long? LastReported { get; set; }
    public DateTime? ReceivedAt { get; set; }
}
