using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Data;
using BusinessPilot.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BusinessPilot.Api.Services;

public sealed class AuthService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    BusinessPilotDbContext db, IConfiguration config)
{
    public async Task<ServiceResult<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (await users.FindByEmailAsync(request.Email) is not null)
            return ServiceResult<AuthResponse>.Duplicate("An account with this email already exists.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = new ApplicationUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.Name.Trim() };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return ServiceResult<AuthResponse>.Failure(string.Join(" ", result.Errors.Select(e => e.Description)));
        var organization = new Organization { Name = request.OrganizationName.Trim() };
        db.Organizations.Add(organization);
        db.Memberships.Add(new Membership { Organization = organization, UserId = user.Id, Role = "Owner" });
        var trialDays = Math.Clamp(config.GetValue("Billing:TrialDays", 14), 1, 90);
        var trialEndsAtUtc = DateTime.UtcNow.AddDays(trialDays);
        db.OrganizationSubscriptions.Add(new OrganizationSubscription {
            Organization = organization, PlanCode = "Free", Status = "Trialing", StartedAtUtc = DateTime.UtcNow,
            CurrentPeriodEndsAtUtc = trialEndsAtUtc
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ServiceResult<AuthResponse>.Success(CreateResponse(user, organization, "Owner"));
    }

    public async Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            return ServiceResult<AuthResponse>.Failure("Email or password is incorrect.");
        var membership = await db.Memberships.Include(x => x.Organization).Where(x => x.UserId == user.Id)
            .OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        return membership is null
            ? ServiceResult<AuthResponse>.Failure("This account is not connected to an organization.")
            : ServiceResult<AuthResponse>.Success(CreateResponse(user, membership.Organization, membership.Role));
    }

    private AuthResponse CreateResponse(ApplicationUser user, Organization organization, string role)
    {
        var expires = DateTime.UtcNow.AddHours(8);
        var secret = config["Jwt:SigningKey"] ?? throw new InvalidOperationException("JWT signing key is not configured.");
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Id), new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(ClaimTypes.Name, user.DisplayName), new Claim("org_id", organization.Id.ToString()), new Claim(ClaimTypes.Role, role) };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims, expires: expires, signingCredentials: credentials);
        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, user.DisplayName, user.Email!, organization.Id, organization.Name, role);
    }
}
