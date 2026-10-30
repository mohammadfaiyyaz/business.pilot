using System.Security.Claims;
using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BusinessPilot.Api.Controllers;

[ApiController, Authorize, Route("api/support")]
public sealed class SupportController(SupportService support) : ControllerBase
{
    [HttpGet("workspace")]
    public async Task<IActionResult> Workspace(CancellationToken ct)
    {
        if (!TryContext(out var orgId, out var userId)) return Forbid();
        var workspace = await support.GetWorkspaceAsync(orgId, userId, CanManage(), ct);
        if (workspace is null) return Forbid();
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(new { workspace, embedCode = CanManage() ? $"<script src=\"{baseUrl}/businesspilot-support-widget.js\" data-widget-key=\"{ReadWidgetKey(workspace)}\" defer></script>" : null });
    }

    [HttpGet("knowledge")]
    public async Task<IActionResult> Knowledge(CancellationToken ct)
    {
        if (!TryContext(out var orgId, out var userId)) return Forbid();
        var data = await support.ListKnowledgeAsync(orgId, userId, ct);
        return data is null ? Forbid() : Ok(data);
    }

    [HttpPost("knowledge")]
    public async Task<IActionResult> CreateKnowledge(SupportKnowledgeRequest request, CancellationToken ct)
    {
        if (!CanManage() || !TryContext(out var orgId, out var userId)) return Forbid();
        var result = await support.CreateKnowledgeAsync(orgId, userId, request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPut("knowledge/{id:guid}")]
    public async Task<IActionResult> UpdateKnowledge(Guid id, SupportKnowledgeRequest request, CancellationToken ct)
    {
        if (!CanManage() || !TryContext(out var orgId, out var userId)) return Forbid();
        var result = await support.UpdateKnowledgeAsync(orgId, userId, id, request, ct);
        if (result.NotFound) return NotFound();
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpDelete("knowledge/{id:guid}")]
    public async Task<IActionResult> ArchiveKnowledge(Guid id, CancellationToken ct)
    {
        if (!CanManage() || !TryContext(out var orgId, out var userId)) return Forbid();
        return await support.ArchiveKnowledgeAsync(orgId, userId, id, ct) ? NoContent() : NotFound();
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(CancellationToken ct)
    {
        if (!TryContext(out var orgId, out var userId)) return Forbid();
        var data = await support.ListConversationsAsync(orgId, userId, ct);
        return data is null ? Forbid() : Ok(data);
    }

    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> Conversation(Guid id, CancellationToken ct)
    {
        if (!TryContext(out var orgId, out var userId)) return Forbid();
        var data = await support.GetConversationAsync(orgId, userId, id, ct);
        return data is null ? NotFound() : Ok(data);
    }

    [HttpPost("messages")]
    public async Task<IActionResult> SendMessage(SupportMessageRequest request, CancellationToken ct)
    {
        if (!TryContext(out var orgId, out var userId)) return Forbid();
        var result = await support.SendInAppAsync(orgId, userId, request, ct);
        if (result.NotFound) return NotFound();
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("conversations/{id:guid}/escalate")]
    public async Task<IActionResult> Escalate(Guid id, CancellationToken ct)
    {
        if (!TryContext(out var orgId, out var userId)) return Forbid();
        return await support.EscalateAsync(orgId, userId, id, ct) ? NoContent() : NotFound();
    }

    [HttpPut("widget/enabled")]
    public async Task<IActionResult> SetWidget(WidgetEnabledRequest request, CancellationToken ct)
    {
        if (!CanManage() || !TryContext(out var orgId, out var userId)) return Forbid();
        return await support.SetWidgetEnabledAsync(orgId, userId, request.Enabled, ct) ? NoContent() : Forbid();
    }

    [AllowAnonymous, EnableCors("SupportWidget"), EnableRateLimiting("support-widget")]
    [HttpGet("widget/{key}")]
    public async Task<IActionResult> PublicWidget(string key, CancellationToken ct)
    {
        var widget = await support.GetPublicWidgetAsync(key, ct);
        return widget is null ? NotFound() : Ok(widget);
    }

    [AllowAnonymous, EnableCors("SupportWidget"), EnableRateLimiting("support-widget")]
    [HttpGet("widget/{key}/conversations/{id:guid}")]
    public async Task<IActionResult> PublicConversation(string key, Guid id, CancellationToken ct)
    {
        var conversation = await support.GetPublicConversationAsync(key, id, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }

    [AllowAnonymous, EnableCors("SupportWidget"), EnableRateLimiting("support-widget")]
    [HttpPost("widget/{key}/messages")]
    public async Task<IActionResult> PublicMessage(string key, WidgetSupportMessageRequest request, CancellationToken ct)
    {
        var result = await support.SendWidgetMessageAsync(key, request, ct);
        if (result.NotFound) return NotFound();
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [AllowAnonymous, EnableCors("SupportWidget"), EnableRateLimiting("support-widget")]
    [HttpPost("widget/{key}/conversations/{id:guid}/escalate")]
    public async Task<IActionResult> PublicEscalate(string key, Guid id, CancellationToken ct)
        => await support.EscalateWidgetAsync(key, id, ct) ? NoContent() : NotFound();

    private bool TryContext(out Guid organizationId, out string userId)
    {
        userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";
        return Guid.TryParse(User.FindFirst("org_id")?.Value, out organizationId) && !string.IsNullOrWhiteSpace(userId);
    }

    private bool CanManage() => User.IsInRole("Owner") || User.IsInRole("Admin");

    private static string? ReadWidgetKey(object workspace)
        => workspace.GetType().GetProperty("widgetKey")?.GetValue(workspace) as string;
}

public sealed record WidgetEnabledRequest(bool Enabled);
