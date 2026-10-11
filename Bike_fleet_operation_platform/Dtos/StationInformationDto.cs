using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace Bike_fleet_operation_platform.Dtos;

public class StationInformationDto
{
    [Required]
    public string Station_id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    [Range(-90, 90)]
    public double Lat { get; set; }
    [Range(-180, 180)]
    public double Lon { get; set; }
    [Range(0,int.MaxValue)]
    public int Capacity { get; set; }
   
}
