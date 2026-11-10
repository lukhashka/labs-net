using Auth.Api.Controllers;
using Auth.Api.Data;
using Auth.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Authentication;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Own database IdentityDb (AppHost), not shared with the business services
var connectionString = builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("Connection string 'IdentityDb' is missing");
builder.Services.AddDbContext<AuthDbContext>(o => o.UseSqlServer(connectionString));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireNonAlphanumeric = true;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

// E-mail confirmation and password reset tokens expire in 2 hours
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));

// AddIdentity registers cookie schemes as default; this service is an API, so JWT becomes the default.
// The scheme validates the tokens this service issues, with the same parameters as all other services.
var tokenValidation = PlatformAuthentication.AuthServiceValidation(builder.Configuration);   // no key outside Development: no start
builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = PlatformAuthentication.AuthService;
        o.DefaultAuthenticateScheme = PlatformAuthentication.AuthService;
        o.DefaultChallengeScheme = PlatformAuthentication.AuthService;
        o.DefaultForbidScheme = PlatformAuthentication.AuthService;
    })
    .AddJwtBearer(PlatformAuthentication.AuthService, o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = tokenValidation;
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<RefreshTokenStore>();
builder.Services.AddSingleton<MockEmailSender>();
builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<MockEmailSender>());

builder.Services.AddHealthChecks().AddSqlServer(connectionString, name: "identity-db",
    failureStatus: HealthStatus.Unhealthy, tags: ["database", "sql", "ready"], timeout: TimeSpan.FromSeconds(5));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.MapDefaultEndpoints();

await IdentitySeeder.SeedAsync(app.Services, app.Configuration, app.Logger);

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Development only: the letters of MockEmailSender, to take the confirmation and reset tokens
    app.MapGet("/auth/dev/mailbox", (string email, MockEmailSender mailbox) => mailbox.For(email)).AllowAnonymous();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
