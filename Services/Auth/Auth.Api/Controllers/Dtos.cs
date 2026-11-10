using System.ComponentModel.DataAnnotations;

namespace Auth.Api.Controllers;

public record RegisterRequest(
    [Required, StringLength(50, MinimumLength = 3)] string UserName,
    [Required, EmailAddress] string Email,
    [Required] string Password,
    [StringLength(200)] string? FullName);

public record LoginRequest([Required] string UserName, [Required] string Password);

// The refresh token normally comes from the HttpOnly cookie; the body is for clients without cookies (Postman, curl)
public record RefreshRequest(string? RefreshToken);

public record VerifyEmailRequest([Required] string UserId, [Required] string Token);

public record EmailRequest([Required, EmailAddress] string Email);

public record ResetPasswordRequest([Required, EmailAddress] string Email, [Required] string Token, [Required] string NewPassword);

public record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword);

public record UpdateUserRequest([StringLength(200)] string? FullName, [Phone] string? PhoneNumber);

public record RoleRequest([Required] string Role);

// Access token in the body, refresh token only in the cookie
public record TokenResponse(string AccessToken, DateTime ExpiresAt, string TokenType = "Bearer");

public record UserDto(string Id, string UserName, string? Email, bool EmailConfirmed, string? FullName, DateTime CreatedAt,
    IList<string> Roles);

public record RefreshTokenDto(Guid Id, DateTime CreatedAt, DateTime ExpiresAt, DateTime? RevokedAt, string? RevokedReason,
    Guid? ReplacedByTokenId, string? CreatedByIp, bool IsActive);
