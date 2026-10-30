using BusinessPilot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessPilot.Api.Controllers;

[ApiController, Authorize, Route("api/reports")]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!CanRead() || !TryOrg(out var orgId)) return Forbid();
        if (!ResolvePeriod(from, to, out var start, out var end, out var error)) return BadRequest(new { message = error });
        return Ok(await reports.GetSummaryAsync(orgId, start, end, ct));
    }

    [HttpGet("export/{type}")]
    public async Task<IActionResult> Export(string type, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!CanRead() || !TryOrg(out var orgId)) return Forbid();
        if (type is not ("sales" or "payments" or "expenses" or "outstanding" or "overdue" or "monthly"))
            return BadRequest(new { message = "Export type must be sales, payments, expenses, outstanding, overdue, or monthly." });
        if (!ResolvePeriod(from, to, out var start, out var end, out var error)) return BadRequest(new { message = error });
        var data = await reports.ExportCsvAsync(orgId, type, start, end, ct);
        return File(data, "text/csv; charset=utf-8", $"businesspilot-{type}-{start:yyyyMMdd}-{end:yyyyMMdd}.csv");
    }

    private bool TryOrg(out Guid id) => Guid.TryParse(User.FindFirst("org_id")?.Value, out id);
    private bool CanRead() => User.IsInRole("Owner") || User.IsInRole("Admin") || User.IsInRole("Accountant");
    private static bool ResolvePeriod(DateOnly? from, DateOnly? to, out DateOnly start, out DateOnly end, out string error)
    {
        end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        start = from ?? new DateOnly(end.Year, end.Month, 1).AddMonths(-5);
        if (start > end) { error = "Start date must be on or before end date."; return false; }
        if (start.AddYears(5) < end) { error = "Report date ranges cannot exceed five years."; return false; }
        error = ""; return true;
    }
}
