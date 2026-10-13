using System.ComponentModel.DataAnnotations;

namespace ProcessingService.Dtos;

public class VehicleTypesDto
{
    [Required]
    [System.Text.Json.Serialization.JsonPropertyName("Vehicle_type_id")]
    [System.ComponentModel.DataAnnotations.Schema.Column("Vehicle_type_id")]
    public string VehicleTypeId { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Form_factor")]
    [System.ComponentModel.DataAnnotations.Schema.Column("Form_factor")]
    public string FormFactor { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("propulsion_type")]
    [System.ComponentModel.DataAnnotations.Schema.Column("propulsion_type")]
    public string PropulsionType { get; set; } = string.Empty;
}
