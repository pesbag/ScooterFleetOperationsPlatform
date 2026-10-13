using Microsoft.AspNetCore.Mvc;
using StationApi.Dtos;
using StationApi.Repository;
namespace StationApi.Controllers;

[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "ADMIN,OPERATOR")]
[Route("api/[controller]")]
public class StationController : ControllerBase
{
    private readonly IStationRepository _repo;
    public StationController(IStationRepository repo)
    {
        _repo = repo;
    }
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GetFilterDto>>> GetAllFilteredAsync([FromQuery] int? minAvailableBike, [FromQuery] int? inRenting, [FromQuery] int? isReturning)
    {
        var result = await _repo.GetStationAsync(minAvailableBike, inRenting, isReturning);
        return Ok(result);
    }
    [HttpGet("{id}")]
    public async Task<ActionResult<AllDataDto>> GetByIdAsync(string id)
    {
        var result = await _repo.GetAllDataByIdAsync(id);
        if(result is null)
        {
            return NotFound();
        }
        return Ok(result);
    }
}
