using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessPilot.Api.Controllers;

[ApiController, Authorize, Route("api/invoices")]
public sealed class InvoicesController(InvoiceService invoices, InvoicePdfService invoicePdfs) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q, CancellationToken ct)
        => TryOrg(out var orgId) ? Ok(await invoices.ListAsync(orgId, status, q, ct)) : Forbid();
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!TryOrg(out var orgId)) return Forbid();
        var invoice = await invoices.GetAsync(orgId, id, ct); return invoice is null ? NotFound() : Ok(invoice);
    }
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid id, CancellationToken ct)
    {
        if (!TryOrg(out var orgId)) return Forbid();
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var pdf = await invoicePdfs.GenerateAsync(orgId, id, userId, ct);
        return pdf is null ? NotFound() : File(pdf.Bytes, "application/pdf", $"{pdf.FileName}.pdf");
    }
    [HttpPost]
    public async Task<IActionResult> Create(CreateInvoiceRequest request, CancellationToken ct)
    {
        if (!CanWrite() || !TryOrg(out var orgId)) return Forbid();
        var result = await invoices.CreateAsync(orgId, request, ct);
        if (!result.Succeeded) return BadRequest(new { message = result.Error });
        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value.Body);
    }
    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> RecordPayment(Guid id, RecordPaymentRequest request, CancellationToken ct)
    {
        if (!CanWrite() || !TryOrg(out var orgId)) return Forbid();
        var result = await invoices.RecordPaymentAsync(orgId, id, request, ct);
        if (result.NotFound) return NotFound();
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (!CanWrite() || !TryOrg(out var orgId)) return Forbid();
        var result = await invoices.CancelAsync(orgId, id, ct);
        if (result.NotFound) return NotFound();
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }
    private bool TryOrg(out Guid id) => Guid.TryParse(User.FindFirst("org_id")?.Value, out id);
    private bool CanWrite() => User.IsInRole("Owner") || User.IsInRole("Admin") || User.IsInRole("Accountant");
}
