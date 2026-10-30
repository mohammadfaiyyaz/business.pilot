using BusinessPilot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessPilot.Api.Controllers;

[ApiController, Authorize, Route("api/dashboard")]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst("org_id")?.Value, out var organizationId)) return Forbid();
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? new DateOnly(end.Year, end.Month, 1).AddMonths(-5);
        if (start > end) return BadRequest(new { message = "Start date must be on or before end date." });
        if (start.AddYears(5) < end) return BadRequest(new { message = "Dashboard date ranges cannot exceed five years." });
        return Ok(await dashboard.GetAsync(organizationId, start, end, ct));
    }
}
