using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BusinessPilot.Api.Controllers;

[ApiController, Route("api/auth"), EnableRateLimiting("auth")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request, ct);
        if (result.Succeeded) return Ok(result.Value);
        return result.Conflict ? Conflict(new { message = result.Error }) : BadRequest(new { message = result.Error });
    }
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : Unauthorized(new { message = result.Error });
    }
}
