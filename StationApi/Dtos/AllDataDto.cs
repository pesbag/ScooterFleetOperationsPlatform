using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace StationApi.Dtos;

public class AllDataDto
{
    [Required]
    public string Station_id { get; set; } = string.Empty;
    [Range(0, int.MaxValue)]
    public int? Num_bikes_available { get; set; }
    [Range(0, int.MaxValue)]
    public int? Nun_vehicles_available { get; set; }
    [Range(0, int.MaxValue)]
    public int? Num_docs_available { get; set; }
    public int? Is_renting { get; set; }
    public int? Is_returning { get; set; }
    public long? Last_reported { get; set; }
    public string Name { get; set; } = string.Empty;
    [Range(-90, 90)]
    public double Lat { get; set; }
    [Range(-180, 180)]
    public double Lon { get; set; }
    [Range(0, int.MaxValue)]
    public int Capacity { get; set; }

}
