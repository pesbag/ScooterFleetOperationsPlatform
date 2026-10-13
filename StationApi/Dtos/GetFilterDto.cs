using System.ComponentModel.DataAnnotations;

namespace StationApi.Dtos;

public class GetFilterDto
{
    [System.Text.Json.Serialization.JsonPropertyName("station_id")]
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "Station_id")]
    public string StationId { get; set; } = string.Empty;
    [Range(0, int.MaxValue)]
    [System.Text.Json.Serialization.JsonPropertyName("num_bikes_available")]
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "Num_bikes_available")]
    public int? NumBikesAvailable { get; set; }
    [Range(0, int.MaxValue)]
    [System.Text.Json.Serialization.JsonPropertyName("num_docs_available")]
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "Num_docs_available")]
    public int? NumDocksAvailable { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("is_renting")]
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "Is_renting")]
    public int? IsRenting { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("is_returning")]
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "Is_returning")]
    public int? IsReturning { get; set; }
    public string Name { get; set; } = string.Empty;
    [Range(-90, 90)]
    public double Lat { get; set; }
    [Range(-180, 180)]
    public double Lon { get; set; }
    [Range(0, int.MaxValue)]
    public int Capacity { get; set; }
}
