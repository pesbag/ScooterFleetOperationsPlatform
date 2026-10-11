using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
namespace ProcessingService.Dtos;

public class StationStatusDto
{
    [Required]
    [BsonId]
    public string Station_id { get; set; } = string.Empty;
    [Range(0,int.MaxValue)]
    public int? Num_bikes_available { get; set; }
    [Range(0, int.MaxValue)]
    public int? Nun_vehicles_available { get; set; }
    [Range(0, int.MaxValue)]
    public int? Num_docs_available { get; set; }
    public int? Is_renting { get; set; }
    public int? Is_returning { get; set; }
    public long? Last_reported { get; set; }
    public DateTime? ReceivedAt { get; set; }

    public bool HasChangedFrom(StationStatusDto? other)
    {
        if (other is null) return true;

        return Is_renting != other.Is_renting
            || Is_returning != other.Is_returning
            || Num_docs_available != other.Num_docs_available
            || Num_bikes_available != other.Num_bikes_available
            || Nun_vehicles_available != other.Nun_vehicles_available;
    }
}
