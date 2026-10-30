using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessPilot.Api.Controllers;

[ApiController, Authorize, Route("api/customers")]
public sealed class CustomersController(CustomerService customers) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => TryOrg(out var orgId) ? Ok(await customers.ListAsync(orgId, q, page, pageSize, ct)) : Forbid();
    [HttpPost]
    public async Task<IActionResult> Create(CustomerRequest request, CancellationToken ct)
    {
        if (!TryOrg(out var orgId)) return Forbid();
        var result = await customers.CreateAsync(orgId, request, ct);
        if (!result.Succeeded) return StatusCode(StatusCodes.Status429TooManyRequests, new { message = result.Error });
        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!TryOrg(out var orgId)) return Forbid();
        var customer = await customers.GetAsync(orgId, id, ct); return customer is null ? NotFound() : Ok(customer);
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CustomerRequest request, CancellationToken ct)
        => !TryOrg(out var orgId) ? Forbid() : await customers.UpdateAsync(orgId, id, request, ct) ? NoContent() : NotFound();
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
        => !TryOrg(out var orgId) ? Forbid() : await customers.ArchiveAsync(orgId, id, ct) ? NoContent() : NotFound();
    private bool TryOrg(out Guid id) => Guid.TryParse(User.FindFirst("org_id")?.Value, out id);
}
