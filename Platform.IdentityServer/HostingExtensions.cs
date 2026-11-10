using Duende.IdentityServer;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.IdentityServer.Data;
using Platform.IdentityServer.Models;
using Platform.ServiceDefaults;

namespace Platform.IdentityServer;

internal static class HostingExtensions
{
    public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();                          // Serilog, OpenTelemetry, health checks
        builder.Services.AddRazorPages();

        // Own database IdentityServerDb, separate from the business databases (isolation of security data)
        var connectionString = builder.Configuration.GetConnectionString("IdentityServerDb")
            ?? throw new InvalidOperationException("Connection string 'IdentityServerDb' is missing");
        var migrationsAssembly = typeof(HostingExtensions).Assembly.GetName().Name;

        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString));

        builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o => o.Password.RequiredLength = 8)
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        builder.Services
            .AddIdentityServer(o =>
            {
                o.Events.RaiseErrorEvents = true;
                o.Events.RaiseFailureEvents = true;
                o.Events.RaiseSuccessEvents = true;
                o.EmitStaticAudienceClaim = false;               // "aud" = the ApiResources of the requested scopes
            })
            .AddAspNetIdentity<ApplicationUser>()              // users and passwords of ASP.NET Identity
            .AddConfigurationStore(o => o.ConfigureDbContext = db =>
                db.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly)))
            .AddOperationalStore(o =>
            {
                o.ConfigureDbContext = db => db.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
                o.EnableTokenCleanup = true;                     // expired grants are removed in the background
            });

        // Development runs over HTTP (the dev certificate is not trusted on this PC). IdentityServer gives its cookies
        // SameSite=None, and a browser drops such a cookie without Secure: the login would loop back to the login page.
        // Lax keeps the cookie on the top-level redirects of the Authorization Code flow. HTTPS in production keeps None.
        if (builder.Environment.IsDevelopment())
        {
            builder.Services.ConfigureApplicationCookie(o => o.Cookie.SameSite = SameSiteMode.Lax);
            builder.Services.ConfigureExternalCookie(o => o.Cookie.SameSite = SameSiteMode.Lax);
            builder.Services.PostConfigure<Duende.IdentityServer.Configuration.IdentityServerOptions>(
                o => o.Authentication.CheckSessionCookieSameSiteMode = SameSiteMode.Lax);
        }

        builder.Services.AddAuthentication();
        return builder.Build();
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.MapDefaultEndpoints();
        if (app.Environment.IsDevelopment())
            app.UseDeveloperExceptionPage();

        app.UseStaticFiles();
        app.UseRouting();
        app.UseIdentityServer();                               // /connect/authorize, /connect/token, discovery
        app.UseAuthorization();
        app.MapRazorPages().RequireAuthorization();
        return app;
    }

    // Migrations of the three contexts, the configuration from Config.cs and the test users
    public static async Task SeedAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<PersistedGrantDbContext>().Database.MigrateAsync();
        var config = sp.GetRequiredService<ConfigurationDbContext>();
        await config.Database.MigrateAsync();

        if (!await config.Clients.AnyAsync())
        {
            config.IdentityResources.AddRange(Config.IdentityResources.Select(r => r.ToEntity()));
            config.ApiScopes.AddRange(Config.ApiScopes.Select(s => s.ToEntity()));
            config.ApiResources.AddRange(Config.ApiResources.Select(r => r.ToEntity()));
            config.Clients.AddRange(Config.Clients.Select(c => c.ToEntity()));
            await config.SaveChangesAsync();
            app.Logger.LogInformation("IdentityServer configuration stored: {Clients} clients", Config.Clients.Count());
        }

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var password = app.Configuration["Seed:UserPassword"];
        if (string.IsNullOrEmpty(password))
            return;
        foreach (var (name, email, fullName) in new[] { ("alice", "alice@campustix.local", "Alice Smith"), ("bob", "bob@campustix.local", "Bob Smith") })
        {
            if (await users.FindByNameAsync(name) is not null)
                continue;
            var user = new ApplicationUser { UserName = name, Email = email, EmailConfirmed = true };
            var created = await users.CreateAsync(user, password);
            if (created.Succeeded)
                await users.AddClaimsAsync(user, [new System.Security.Claims.Claim("name", fullName), new System.Security.Claims.Claim("email", email)]);
        }
    }
}
