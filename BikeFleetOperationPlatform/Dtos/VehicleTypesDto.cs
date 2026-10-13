using System.ComponentModel.DataAnnotations;

namespace BikeFleetOperationPlatform.Dtos;

public class VehicleTypesDto
{
    [Required]
    [System.Text.Json.Serialization.JsonPropertyName("Vehicle_type_id")]
    public string VehicleTypeId { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Form_factor")]
    public string FormFactor { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("propulsion_type")]
    public string PropulsionType { get; set; } = string.Empty;
}
