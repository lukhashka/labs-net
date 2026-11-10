using System.Security.Claims;
using Orders.Bll.Services;
using Platform.ServiceDefaults.Authentication;

namespace Orders.Api.Security;

// Claims of the token in the business logic: a customer sees and creates only own orders,
// staff (orders:update: manager, admin) works with all orders.
public sealed class CurrentUser(IHttpContextAccessor accessor, ICustomerService customers)
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public string? Id => User.FindFirst("sub")?.Value;

    public bool IsStaff => PermissionResolver.GetPermissions(User).Contains("orders:update");

    // The customer profile linked to the user (Customers.ExternalUserId = sub). Created on the first order
    // from the e-mail and the name in the token, so a newly registered user can order at once.
    public async Task<long?> CustomerIdAsync(bool createIfMissing, CancellationToken ct)
    {
        if (Id is null)
            return null;

        var email = User.FindFirst("email")?.Value;
        var name = User.FindFirst("name")?.Value ?? User.FindFirst("preferred_username")?.Value
                   ?? User.FindFirst("unique_name")?.Value ?? email;
        return await customers.GetCustomerIdForUserAsync(Id, createIfMissing ? email : null, name, ct);
    }
}
