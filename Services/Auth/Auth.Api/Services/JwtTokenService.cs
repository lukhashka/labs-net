using System.Security.Claims;
using System.Security.Cryptography;
using Auth.Api.Data;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.ServiceDefaults.Authentication;

namespace Auth.Api.Services;

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public interface IJwtTokenService
{
    AccessToken GenerateAccessToken(ApplicationUser user, IEnumerable<string> roles);
    string GenerateRefreshToken();
    Task<ClaimsPrincipal?> ValidateTokenAsync(string token);
}

// Access token: short (15 min), signed with HS256. Every service validates it with the same secret locally,
// roles are inside, so authorization needs no call to this service or to the database.
public sealed class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _validation = PlatformAuthentication.AuthServiceValidation(configuration);
    private readonly TimeSpan _lifetime = TimeSpan.FromMinutes(configuration.GetValue("Jwt:AccessTokenMinutes", 15));

    public AccessToken GenerateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName!),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(PlatformAuthentication.RoleClaim, r)));

        var expiresAt = DateTime.UtcNow.Add(_lifetime);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _validation.ValidIssuer,
            Audience = _validation.ValidAudience,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(_validation.IssuerSigningKey, SecurityAlgorithms.HmacSha256)
        });
        return new AccessToken(token, expiresAt);
    }

    // 64 random bytes: not a JWT, it means nothing without the record in the database
    public string GenerateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        var result = await _handler.ValidateTokenAsync(token, _validation);
        return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : null;
    }

    public static string Hash(string refreshToken)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken)));
}
