using System.Security.Claims;
using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Data;
using BusinessPilot.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Controllers;

[ApiController, Authorize, Route("api/profile")]
public sealed class ProfileController(UserManager<ApplicationUser> users, BusinessPilotDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryContext(out var userId, out var organizationId)) return Forbid();
        var user = await users.FindByIdAsync(userId);
        var membership = await db.Memberships.AsNoTracking().Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.OrganizationId == organizationId, ct);
        if (user is null || membership is null) return Forbid();
        return Ok(new ProfileResponse(user.DisplayName, user.Email ?? "", membership.Organization.Name, membership.Role));
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateProfileRequest request, CancellationToken ct)
    {
        if (!TryContext(out var userId, out var organizationId)) return Forbid();
        var membership = await db.Memberships.Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.OrganizationId == organizationId, ct);
        if (membership is null || !new[] { "Owner", "Admin" }.Contains(membership.Role)) return Forbid();

        var user = await users.FindByIdAsync(userId);
        if (user is null) return Forbid();
        var email = request.Email.Trim();
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null && existing.Id != user.Id)
            return Conflict(new { message = "That email address is already in use." });
        var usernameOwner = await users.FindByNameAsync(email);
        if (usernameOwner is not null && usernameOwner.Id != user.Id)
            return Conflict(new { message = "That email address is already in use." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var setEmail = await users.SetEmailAsync(user, email);
        if (!setEmail.Succeeded) return BadRequest(new { message = string.Join(" ", setEmail.Errors.Select(x => x.Description)) });
        var setUserName = await users.SetUserNameAsync(user, email);
        if (!setUserName.Succeeded) return BadRequest(new { message = string.Join(" ", setUserName.Errors.Select(x => x.Description)) });

        user.DisplayName = request.Name.Trim();
        membership.Organization.Name = request.OrganizationName.Trim();
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(new { message = string.Join(" ", result.Errors.Select(x => x.Description)) });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new ProfileResponse(user.DisplayName, user.Email ?? email, membership.Organization.Name, membership.Role));
    }

    private bool TryContext(out string userId, out Guid organizationId)
    {
        userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";
        return Guid.TryParse(User.FindFirst("org_id")?.Value, out organizationId) && !string.IsNullOrWhiteSpace(userId);
    }
}

public sealed record ProfileResponse(string Name, string Email, string OrganizationName, string Role);
