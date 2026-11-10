using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Data;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

// Only the SHA-256 hash of a refresh token is stored: a leaked database does not give working tokens
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedByTokenId { get; set; }          // rotation: which token replaced this one

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}

// AspNetUsers, AspNetRoles, AspNetUserRoles, ... from IdentityDbContext plus RefreshTokens
public class AuthDbContext(DbContextOptions<AuthDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);                     // tables of ASP.NET Identity

        builder.Entity<RefreshToken>(t =>
        {
            t.ToTable("RefreshTokens");
            t.HasKey(r => r.Id);
            t.Property(r => r.TokenHash).IsRequired().HasMaxLength(64);
            t.HasIndex(r => r.TokenHash).IsUnique();       // lookup by the hash of the cookie value
            t.Property(r => r.UserId).IsRequired();
            t.Property(r => r.CreatedByIp).HasMaxLength(64);
            t.Property(r => r.RevokedReason).HasMaxLength(200);
            t.Ignore(r => r.IsActive);
            t.HasOne<ApplicationUser>().WithMany(u => u.RefreshTokens).HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApplicationUser>().Property(u => u.FullName).HasMaxLength(200);
    }
}
