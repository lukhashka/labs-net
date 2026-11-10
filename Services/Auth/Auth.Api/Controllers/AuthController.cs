using Auth.Api.Data;
using Auth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Platform.ServiceDefaults.Authentication;

namespace Auth.Api.Controllers;

[ApiController]
[Route("auth")]
[Produces("application/json")]
public class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IJwtTokenService tokens,
    RefreshTokenStore refreshTokens,
    IEmailSender email,
    ILogger<AuthController> logger) : ControllerBase
{
    public const string RefreshCookie = "refreshToken";

    // New user gets the role User; login is blocked until the e-mail is confirmed
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var user = new ApplicationUser { UserName = request.UserName, Email = request.Email, FullName = request.FullName };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            return IdentityError(created);

        await users.AddToRoleAsync(user, Roles.User);
        await SendConfirmationAsync(user, ct);
        logger.LogInformation("User {UserId} registered, confirmation sent", user.Id);
        return Created($"/users/{user.Id}", new { userId = user.Id, message = "Check the e-mail to confirm the account" });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByNameAsync(request.UserName) ?? await users.FindByEmailAsync(request.UserName);
        if (user is null)
            return Problem(statusCode: 401, title: "Invalid user name or password");

        // lockoutOnFailure: after 5 wrong passwords the account is locked for 15 minutes
        var check = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
            return Problem(statusCode: 403, title: "Account is locked after too many failed attempts");
        if (check.IsNotAllowed)                              // SignIn.RequireConfirmedEmail: the password is right
            return Problem(statusCode: 403, title: "E-mail is not confirmed");
        if (!check.Succeeded)
            return Problem(statusCode: 401, title: "Invalid user name or password");

        return Ok(await IssueTokensAsync(user, ct));
    }

    // Rotation: the old refresh token is revoked, a new pair is issued
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(RefreshRequest? request, CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie] ?? request?.RefreshToken;
        if (string.IsNullOrEmpty(raw))
            return Problem(statusCode: 401, title: "Refresh token is missing");

        var rotation = await refreshTokens.RotateAsync(raw, ClientIp(), ct);
        if (rotation.User is null)
        {
            Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UtcNow));
            return Problem(statusCode: 401, title: rotation.Error);
        }

        var access = tokens.GenerateAccessToken(rotation.User, await users.GetRolesAsync(rotation.User));
        var stored = await refreshTokens.FindAsync(rotation.NewToken!, ct);
        SetRefreshCookie(rotation.NewToken!, stored!.ExpiresAt);
        return Ok(new TokenResponse(access.Token, access.ExpiresAt));
    }

    [HttpPost("revoke-token")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeToken(RefreshRequest? request, CancellationToken ct)
    {
        var raw = request?.RefreshToken ?? Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(raw))
            return Problem(statusCode: 400, title: "Refresh token is missing");

        var token = await refreshTokens.FindAsync(raw, ct);
        if (token is null)
            return Problem(statusCode: 400, title: "Unknown refresh token");
        if (token.UserId != CurrentUserId() && !User.IsInRole(Roles.Admin))
            return Forbid();

        await refreshTokens.RevokeAsync(token, "Revoked by the user", ct);
        return NoContent();
    }

    // The refresh token is revoked; the access token stays valid until it expires (15 min), that is why it is short
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest? request, CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie] ?? request?.RefreshToken;
        if (!string.IsNullOrEmpty(raw) && await refreshTokens.FindAsync(raw, ct) is { } token && token.UserId == CurrentUserId())
            await refreshTokens.RevokeAsync(token, "Logout", ct);

        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UtcNow));
        return NoContent();
    }

    // The token is one-time: the security stamp changes after the confirmation
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId);
        if (user is null || user.EmailConfirmed)
            return Problem(statusCode: 400, title: "Invalid or used confirmation token");

        var result = await users.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
            return Problem(statusCode: 400, title: "Invalid or expired confirmation token");

        await users.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    // Always 202: the answer does not tell whether such an account exists
    [HttpPost("resend-verification")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendVerification(EmailRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is { EmailConfirmed: false })
            await SendConfirmationAsync(user, ct);
        return Accepted();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await email.SendAsync(user.Email!, "Reset the password",
                $"POST /auth/reset-password with email={user.Email} token={token}", ct);
        }
        return Accepted();
    }

    // After the reset every refresh token of the user is revoked: sessions on other devices end
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
            return Problem(statusCode: 400, title: "Invalid or expired reset token");

        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);   // changes the security stamp
        if (!result.Succeeded)
            return IdentityError(result);

        var revoked = await refreshTokens.RevokeAllAsync(user.Id, "Password reset", ct);
        logger.LogInformation("Password of user {UserId} reset, {Revoked} refresh tokens revoked", user.Id, revoked);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(CurrentUserId()!);
        if (user is null)
            return Unauthorized();

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return IdentityError(result);

        await refreshTokens.RevokeAllAsync(user.Id, "Password changed", ct);
        return NoContent();
    }

    private async Task<TokenResponse> IssueTokensAsync(ApplicationUser user, CancellationToken ct)
    {
        var access = tokens.GenerateAccessToken(user, await users.GetRolesAsync(user));
        var (refresh, expiresAt) = await refreshTokens.CreateAsync(user, ClientIp(), ct);
        SetRefreshCookie(refresh, expiresAt);
        logger.LogInformation("User {UserId} logged in", user.Id);
        return new TokenResponse(access.Token, access.ExpiresAt);
    }

    private async Task SendConfirmationAsync(ApplicationUser user, CancellationToken ct)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await email.SendAsync(user.Email!, "Confirm the e-mail",
            $"POST /auth/verify-email with userId={user.Id} token={token}", ct);
    }

    // HttpOnly: JavaScript of the page cannot read it (XSS); SameSite=Strict: not sent by other sites (CSRF);
    // Path=/auth: the cookie goes only to this service
    private void SetRefreshCookie(string token, DateTime expiresAt)
        => Response.Cookies.Append(RefreshCookie, token, CookieOptions(expiresAt));

    private CookieOptions CookieOptions(DateTime expiresAt) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps || !HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/auth",
        Expires = expiresAt
    };

    private string? CurrentUserId() => User.FindFirst(PlatformAuthentication.UserIdClaim)?.Value;

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IActionResult IdentityError(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
            return Problem(statusCode: 409, title: string.Join(" ", result.Errors.Select(e => e.Description)));

        foreach (var error in result.Errors)
            ModelState.AddModelError(error.Code, error.Description);
        return ValidationProblem(ModelState);
    }
}
