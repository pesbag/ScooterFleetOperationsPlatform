using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bike_fleet_operation_platform.Dtos;

public class VehicleTypesDto
{
    [Required]
    public string Vehicle_type_id { get; set; } = string.Empty;
    public string Form_factor { get; set; } = string.Empty;
    public string propulsion_type { get; set; } = string.Empty;
}
