using System.ComponentModel.DataAnnotations;

namespace BikeFleetOperationPlatform.Dtos;

public class StationInformationDto
{
    [Required]
    [System.Text.Json.Serialization.JsonPropertyName("Station_id")]
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    [Range(-90, 90)]
    public double Lat { get; set; }
    [Range(-180, 180)]
    public double Lon { get; set; }
    [Range(0,int.MaxValue)]
    public int Capacity { get; set; }
   
}
