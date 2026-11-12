using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Platform.ServiceDefaults.Authentication;

// Lab 09: the platform accepts tokens of three identity providers (the three parts of the lab).
// Every service validates a token LOCALLY with the keys of its issuer, it never calls the provider per request.
//   AuthService    - own service on ASP.NET Identity, HS256 with a shared secret (Part 1)
//   IdentityServer - Duende IdentityServer, keys from /.well-known/openid-configuration (Part 2)
//   Keycloak       - realm DevRealmDrivers, AddKeycloakJwtBearer in Program.cs of the service (Part 3)
// The default scheme "Bearer" only looks at the issuer of the token and forwards to one of them.
public static class PlatformAuthentication
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
    public const string AuthService = "AuthService";
    public const string IdentityServer = "IdentityServer";
    public const string Keycloak = "Keycloak";
    public const string KeycloakRealm = "DevRealmDrivers";

    // claim names of the Auth Service token; inbound claim mapping is off, so the names stay as in the token
    public const string UserIdClaim = JwtRegisteredClaimNames.Sub;
    public const string UserNameClaim = JwtRegisteredClaimNames.UniqueName;
    public const string RoleClaim = "role";

    public static AuthenticationBuilder AddPlatformAuthentication(this IHostApplicationBuilder builder, string identityServerAudience)
    {
        var configuration = builder.Configuration;
        var authServiceIssuer = configuration["Jwt:Issuer"] ?? "campustix-auth";
        var authServiceValidation = AuthServiceValidation(configuration);   // at start: no key -> the service does not start

        return builder.Services
            .AddAuthentication(Scheme)
            .AddPolicyScheme(Scheme, "Auth Service, IdentityServer or Keycloak", o =>
                o.ForwardDefaultSelector = context => SelectScheme(context, authServiceIssuer))
            .AddJwtBearer(AuthService, o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = authServiceValidation;
            })
            .AddJwtBearer(IdentityServer, o =>
            {
                // the address comes from Aspire service discovery: services__identityserver__http__0
                o.Authority = configuration["services:identityserver:http:0"] ?? configuration["IdentityServer:Authority"];
                o.Audience = identityServerAudience;                       // name of the ApiResource: orders_api
                o.RequireHttpsMetadata = RequireHttpsMetadata(builder);
                o.MapInboundClaims = false;
                o.TokenValidationParameters.ValidTypes = ["at+jwt"];       // access tokens only, not id tokens
            });
    }

    // Metadata and keys of IdentityServer and Keycloak only over HTTPS, except Development and the Kubernetes cluster
    // of Lab 10: there the providers answer on http://identityserver:8080 and http://keycloak:8080 inside the cluster,
    // and the AppHost passes Auth__RequireHttpsMetadata=false in publish mode. The issuer is still checked.
    public static bool RequireHttpsMetadata(IHostApplicationBuilder builder) =>
        builder.Configuration.GetValue("Auth:RequireHttpsMetadata", !builder.Environment.IsDevelopment());

    // Shared by the Auth Service (it issues the tokens) and the services (they validate them)
    public static TokenValidationParameters AuthServiceValidation(IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt");
        var secret = jwt["SecretKey"];
        if (string.IsNullOrEmpty(secret))
        {
            // AppHost passes a generated key, Kubernetes the Secret platform-jwt (Lab 10). A known key outside
            // Development would let anybody sign tokens, so the service refuses to start without one.
            if (!string.Equals(configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"], "Development",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Jwt:SecretKey is not configured");
            secret = "development-only-key-set-Jwt__SecretKey-in-AppHost-0000000000";
        }

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"] ?? "campustix-auth",
            ValidateAudience = true,
            ValidAudience = jwt["Audience"] ?? "campustix-api",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),                       // a 15-minute token must really end
            NameClaimType = UserNameClaim,
            RoleClaimType = RoleClaim
        };
    }

    private static string SelectScheme(HttpContext context, string authServiceIssuer)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthService;                                        // no token: any scheme answers 401

        try
        {
            var issuer = new JsonWebTokenHandler().ReadJsonWebToken(header["Bearer ".Length..].Trim()).Issuer;
            if (issuer == authServiceIssuer)
                return AuthService;
            return issuer.Contains("/realms/", StringComparison.Ordinal) ? Keycloak : IdentityServer;
        }
        catch (ArgumentException)
        {
            return AuthService;                                        // not a JWT: validation fails with 401
        }
    }
}
