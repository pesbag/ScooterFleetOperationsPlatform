using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bike_fleet_operation_platform.Dtos;

public class StationStatusDto
{
    [Required]
    public string Station_id { get; set; } = string.Empty;
    [Range(0,int.MaxValue)]
    public int? Num_bikes_available { get; set; }
    [Range(0, int.MaxValue)]
    [System.Text.Json.Serialization.JsonPropertyName("num_vehicles_available")]
    public int? Nun_vehicles_available { get; set; }
    [Range(0, int.MaxValue)]
    [System.Text.Json.Serialization.JsonPropertyName("num_docks_available")]
    public int? Num_docs_available { get; set; }
    [System.Text.Json.Serialization.JsonConverter(typeof(Bike_fleet_operation_platform.Services.GbfsFlagConverter))]
    public int? Is_renting { get; set; }
    [System.Text.Json.Serialization.JsonConverter(typeof(Bike_fleet_operation_platform.Services.GbfsFlagConverter))]
    public int? Is_returning { get; set; }
    public long? Last_reported { get; set; }
    public DateTime? ReceivedAt { get; set; }
}
