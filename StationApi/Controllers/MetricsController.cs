using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StationApi.Services;
namespace StationApi.Controllers;
[ApiController, Authorize(Roles = "ADMIN,OPERATOR"), Route("api/metrics")]
public class MetricsController(FleetDataService data, MetricsService metrics) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token) => Ok(await metrics.Summary(await data.GetStations(token), token));
}
