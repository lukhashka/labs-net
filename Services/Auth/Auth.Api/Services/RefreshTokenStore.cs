using Auth.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Services;

public sealed record RotationResult(ApplicationUser? User, string? NewToken, string? Error);

// Refresh tokens live 7 days in the database with dates of creation, expiry and revocation.
// Rotation: every refresh revokes the old token and creates a new one. Using a revoked token again means
// it was stolen: all active tokens of the user are revoked (reuse detection).
public sealed class RefreshTokenStore(AuthDbContext db, IJwtTokenService tokens, IConfiguration configuration,
    ILogger<RefreshTokenStore> logger)
{
    private readonly TimeSpan _lifetime = TimeSpan.FromDays(configuration.GetValue("Jwt:RefreshTokenDays", 7));

    public async Task<(string Token, DateTime ExpiresAt)> CreateAsync(ApplicationUser user, string? ip, CancellationToken ct)
    {
        var (raw, entity) = NewToken(user.Id, ip);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return (raw, entity.ExpiresAt);
    }

    public async Task<RotationResult> RotateAsync(string raw, string? ip, CancellationToken ct)
    {
        var current = await FindAsync(raw, ct);
        if (current is null)
            return new RotationResult(null, null, "Unknown refresh token");

        if (current.RevokedAt is not null)
        {
            await RevokeAllAsync(current.UserId, "Reuse of a revoked token", ct);
            logger.LogWarning("Revoked refresh token of user {UserId} was used again, all tokens revoked", current.UserId);
            return new RotationResult(null, null, "Refresh token was revoked");
        }
        if (current.ExpiresAt <= DateTime.UtcNow)
            return new RotationResult(null, null, "Refresh token expired");

        var (newRaw, replacement) = NewToken(current.UserId, ip);
        current.RevokedAt = DateTime.UtcNow;
        current.RevokedReason = "Replaced by rotation";
        current.ReplacedByTokenId = replacement.Id;
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);                     // the old one is revoked and the new one stored together

        var user = await db.Users.SingleAsync(u => u.Id == current.UserId, ct);
        return new RotationResult(user, newRaw, null);
    }

    public async Task<RefreshToken?> FindAsync(string raw, CancellationToken ct)
    {
        var hash = JwtTokenService.Hash(raw);
        return await db.RefreshTokens.SingleOrDefaultAsync(r => r.TokenHash == hash, ct);
    }

    public async Task<bool> RevokeAsync(RefreshToken token, string reason, CancellationToken ct)
    {
        if (!token.IsActive)
            return false;
        token.RevokedAt = DateTime.UtcNow;
        token.RevokedReason = reason;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> RevokeAllAsync(string userId, string reason, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, now).SetProperty(r => r.RevokedReason, reason), ct);
    }

    private (string Raw, RefreshToken Entity) NewToken(string userId, string? ip)
    {
        var raw = tokens.GenerateRefreshToken();
        return (raw, new RefreshToken
        {
            UserId = userId,
            TokenHash = JwtTokenService.Hash(raw),
            ExpiresAt = DateTime.UtcNow.Add(_lifetime),
            CreatedByIp = ip
        });
    }
}
