using BusinessPilot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BusinessPilot.Api.Controllers;

[ApiController, Authorize, Route("api/billing")]
public sealed class BillingController(BillingService billing, RazorpayBillingService razorpay) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst("org_id")?.Value, out var organizationId)) return Forbid();
        return Ok(await billing.GetOverviewAsync(organizationId, ct, razorpay.IsConfigured));
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(BillingCheckoutRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst("org_id")?.Value, out var organizationId)) return Forbid();
        if (!User.IsInRole("Owner") && !User.IsInRole("Admin")) return Forbid();
        var result = await razorpay.CreateCheckoutAsync(organizationId, request.PlanCode, ct);
        if (result.NotFound) return NotFound();
        if (result.Conflict) return Conflict(new { message = result.Error });
        if (!result.Succeeded)
            return StatusCode(razorpay.IsConfigured ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable,
                new { message = result.Error });
        return Ok(result.Value);
    }

    [AllowAnonymous, HttpPost("webhook")]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();
        var accepted = await razorpay.HandleWebhookAsync(buffer.ToArray(), signature, ct);
        return accepted ? Ok() : BadRequest(new { message = "Invalid Razorpay webhook signature or temporary reconciliation error." });
    }
}

public sealed record BillingCheckoutRequest(string PlanCode);
