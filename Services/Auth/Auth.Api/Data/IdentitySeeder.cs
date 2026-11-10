using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Data;

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

// On start: migrations, the roles User and Admin, and in Development an administrator from appsettings.Development.json
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.MigrateAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.User, Roles.Admin })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        var userName = configuration["Seed:AdminUserName"];
        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
            return;

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.FindByNameAsync(userName) is not null)
            return;

        var admin = new ApplicationUser { UserName = userName, Email = configuration["Seed:AdminEmail"], EmailConfirmed = true, FullName = "Administrator" };
        var created = await userManager.CreateAsync(admin, password);
        if (created.Succeeded)
        {
            await userManager.AddToRolesAsync(admin, [Roles.User, Roles.Admin]);
            logger.LogInformation("Administrator {UserName} created", userName);
        }
    }
}
