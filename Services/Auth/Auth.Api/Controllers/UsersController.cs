using Auth.Api.Data;
using Auth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platform.ServiceDefaults.Authentication;

namespace Auth.Api.Controllers;

// Admin endpoints: [Authorize(Roles = "Admin")]. Owner endpoints: the id from the token must equal {id}, or Admin.
[ApiController]
[Route("users")]
[Authorize]
[Produces("application/json")]
public class UsersController(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles,
    AuthDbContext db, RefreshTokenStore refreshTokens) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken ct)
    {
        var list = await users.Users.OrderBy(u => u.UserName).ToListAsync(ct);
        var result = new List<UserDto>();
        foreach (var u in list)
            result.Add(ToDto(u, await users.GetRolesAsync(u)));
        return result;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetById(string id)
    {
        if (!IsOwnerOrAdmin(id))
            return Forbid();
        var user = await users.FindByIdAsync(id);
        return user is null ? NotFound() : ToDto(user, await users.GetRolesAsync(user));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateUserRequest request)
    {
        if (!IsOwnerOrAdmin(id))
            return Forbid();
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        user.FullName = request.FullName;
        user.PhoneNumber = request.PhoneNumber;
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? NoContent() : Problem(statusCode: 400, title: string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        await users.DeleteAsync(user);                       // refresh tokens are removed by the cascade
        return NoContent();
    }

    [HttpGet("{id}/refresh-tokens")]
    public async Task<ActionResult<IReadOnlyList<RefreshTokenDto>>> GetRefreshTokens(string id, CancellationToken ct)
    {
        if (!IsOwnerOrAdmin(id))
            return Forbid();
        var list = await db.RefreshTokens.Where(r => r.UserId == id).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return list.Select(r => new RefreshTokenDto(r.Id, r.CreatedAt, r.ExpiresAt, r.RevokedAt, r.RevokedReason,
            r.ReplacedByTokenId, r.CreatedByIp, r.IsActive)).ToList();
    }

    [HttpGet("{id}/roles")]
    public async Task<ActionResult<IList<string>>> GetRoles(string id)
    {
        var user = await users.FindByIdAsync(id);
        return user is null ? NotFound() : Ok(await users.GetRolesAsync(user));
    }

    [HttpPost("{id}/roles")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AddRole(string id, RoleRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        if (!await roles.RoleExistsAsync(request.Role))
            return Problem(statusCode: 400, title: $"Role '{request.Role}' does not exist");

        await users.AddToRoleAsync(user, request.Role);
        // roles are inside the access token: the new role works after the next login or refresh
        return NoContent();
    }

    [HttpDelete("{id}/roles/{role}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> RemoveRole(string id, string role, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        await users.RemoveFromRoleAsync(user, role);
        await refreshTokens.RevokeAllAsync(id, $"Role {role} removed", ct);   // the old role must not come back with a refresh
        return NoContent();
    }

    private bool IsOwnerOrAdmin(string id)
        => User.FindFirst(PlatformAuthentication.UserIdClaim)?.Value == id || User.IsInRole(Roles.Admin);

    private static UserDto ToDto(ApplicationUser u, IList<string> userRoles)
        => new(u.Id, u.UserName!, u.Email, u.EmailConfirmed, u.FullName, u.CreatedAt, userRoles);
}
