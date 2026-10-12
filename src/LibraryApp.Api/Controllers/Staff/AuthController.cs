using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using LibraryApp.Api.Auth;
using LibraryApp.Api.ErrorHandling;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LibraryApp.Api.Controllers.Staff;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Logs in with a corporate email and password and returns a JWT access token (MEM-02).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimiting.PolicyName)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var response = await authService.LoginAsync(request, ct);
        if (response is not null)
            return Ok(response);

        var error = StaffErrors.InvalidCredentials;
        var problem = GlobalExceptionHandler.Problem(StatusCodes.Status401Unauthorized, "Unauthorized", error.Message, error.Code);
        problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status401Unauthorized,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>Changes the generated initial password before an imported staff account can use the API.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeInitialPassword(ChangeInitialPasswordRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) ||
            !await authService.ChangeInitialPasswordAsync(userId, request, ct))
        {
            var problem = GlobalExceptionHandler.Problem(StatusCodes.Status400BadRequest, "Password change failed",
                "The current password is invalid or this account does not require an initial password change.",
                "staff.initial_password_change_failed");
            problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" },
            };
        }

        return NoContent();
    }
}
