using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessPilot.Api.Controllers;

[ApiController, Authorize, Route("api/expenses")]
public sealed class ExpensesController(ExpenseService expenses) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!TryOrg(out var orgId)) return Forbid();
        if (from.HasValue && to.HasValue && from > to) return BadRequest(new { message = "The start date must be before the end date." });
        return Ok(await expenses.ListAsync(orgId, q, from, to, ct));
    }
    [HttpPost]
    public async Task<IActionResult> Create(ExpenseRequest request, CancellationToken ct)
    {
        if (!CanWrite() || !TryOrg(out var orgId)) return Forbid();
        return Ok(await expenses.CreateAsync(orgId, request, ct));
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ExpenseRequest request, CancellationToken ct)
    {
        if (!CanWrite() || !TryOrg(out var orgId)) return Forbid();
        var expense = await expenses.UpdateAsync(orgId, id, request, ct); return expense is null ? NotFound() : Ok(expense);
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        if (!CanWrite() || !TryOrg(out var orgId)) return Forbid();
        return await expenses.ArchiveAsync(orgId, id, ct) ? NoContent() : NotFound();
    }
    private bool TryOrg(out Guid id) => Guid.TryParse(User.FindFirst("org_id")?.Value, out id);
    private bool CanWrite() => User.IsInRole("Owner") || User.IsInRole("Admin") || User.IsInRole("Accountant");
}
