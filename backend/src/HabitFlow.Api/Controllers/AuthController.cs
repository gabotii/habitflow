using HabitFlow.Api.Web;
using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Auth;
using HabitFlow.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HabitFlow.Api.Controllers;

/// <summary>
/// Session management with an HttpOnly, SameSite=Strict cookie. Clients first call <c>GET /csrf</c> and then send the
/// <c>XSRF-TOKEN</c> cookie value in the <c>X-XSRF-TOKEN</c> header on every POST/PUT/DELETE.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(AccountService accounts) : ControllerBase
{
    /// <summary>Issues (or refreshes) the anti-forgery token cookie for the current session.</summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Csrf()
    {
        Web.Csrf.Issue(HttpContext, User);
        return NoContent();
    }

    /// <summary>Optional features available in this deployment (they depend on e-mail configuration).</summary>
    [HttpGet("capabilities")]
    [AllowAnonymous]
    public AuthCapabilitiesDto Capabilities() => accounts.Capabilities;

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = await accounts.RegisterAsync(request);
        await SessionCookies.SignInAsync(HttpContext, user, persistent: true);
        return StatusCode(StatusCodes.Status201Created, AccountService.ToDto(user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<UserDto> Login(LoginRequest request)
    {
        var user = await accounts.ValidateCredentialsAsync(request);
        await SessionCookies.SignInAsync(HttpContext, user, request.RememberMe);
        return AccountService.ToDto(user);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await SessionCookies.SignOutAsync(HttpContext);
        return NoContent();
    }

    /// <summary>Always 202, whether or not the account exists. 503 when e-mail is not configured.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.PasswordReset)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await accounts.ForgotPasswordAsync(request, ct);
        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        await accounts.ResetPasswordAsync(request);
        return NoContent();
    }
}

/// <summary>The authenticated user's own account.</summary>
[ApiController]
[Route("api/v1/users/me")]
[Produces("application/json")]
public sealed class UsersController(AccountService accounts, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<UserDto> Get() => AccountService.ToDto(await accounts.GetUserAsync(currentUser.UserId));

    /// <summary>Display name, time zone (changes what "today" means from now on; history keeps its dates) and e-mail consent.</summary>
    [HttpPut]
    public Task<UserDto> Update(UpdateProfileRequest request) => accounts.UpdateProfileAsync(currentUser.UserId, request);

    /// <summary>Changes the password. Other sessions are signed out; this one gets a fresh cookie.</summary>
    [HttpPost("password")]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await accounts.ChangePasswordAsync(currentUser.UserId, request);
        await SessionCookies.SignInAsync(HttpContext, user, persistent: true);
        return NoContent();
    }

    /// <summary>Permanently deletes the account and all of its data. Requires the current password.</summary>
    [HttpDelete]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(DeleteAccountRequest request)
    {
        await accounts.DeleteAccountAsync(currentUser.UserId, request);
        await SessionCookies.SignOutAsync(HttpContext);
        return NoContent();
    }
}
