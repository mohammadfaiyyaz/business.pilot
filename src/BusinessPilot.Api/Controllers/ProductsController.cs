using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessPilot.Api.Controllers;

[ApiController, Authorize, Route("api/products")]
public sealed class ProductsController(ProductService products) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, CancellationToken ct)
        => TryOrg(out var orgId) ? Ok(await products.ListAsync(orgId, q, ct)) : Forbid();
    [HttpPost]
    public async Task<IActionResult> Create(ProductRequest request, CancellationToken ct)
    {
        if (!CanWrite() || !TryOrg(out var orgId)) return Forbid();
        var result = await products.CreateAsync(orgId, request, ct);
        if (result.Conflict) return Conflict(new { message = result.Error });
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ProductRequest request, CancellationToken ct)
    {
        if (!CanWrite() || !TryOrg(out var orgId)) return Forbid();
        var result = await products.UpdateAsync(orgId, id, request, ct);
        if (result.NotFound) return NotFound();
        if (result.Conflict) return Conflict(new { message = result.Error });
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }
    [HttpPost("{id:guid}/stock-adjustments")]
    public async Task<IActionResult> AdjustStock(Guid id, StockAdjustmentRequest request, CancellationToken ct)
    {
        if (!CanWrite() || !TryOrg(out var orgId)) return Forbid();
        var result = await products.AdjustStockAsync(orgId, id, request, ct);
        if (result.NotFound) return NotFound();
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }
    [HttpGet("{id:guid}/stock-movements")]
    public async Task<IActionResult> Movements(Guid id, CancellationToken ct)
    {
        if (!TryOrg(out var orgId)) return Forbid();
        var movements = await products.MovementsAsync(orgId, id, ct); return movements is null ? NotFound() : Ok(movements);
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        if (!CanWrite() || !TryOrg(out var orgId)) return Forbid();
        return await products.ArchiveAsync(orgId, id, ct) ? NoContent() : NotFound();
    }
    private bool TryOrg(out Guid id) => Guid.TryParse(User.FindFirst("org_id")?.Value, out id);
    private bool CanWrite() => User.IsInRole("Owner") || User.IsInRole("Admin") || User.IsInRole("Accountant");
}
