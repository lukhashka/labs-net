using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Authentication;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// The same three token issuers as the other services; permissions are checked locally per endpoint
builder.AddPlatformAuthentication(identityServerAudience: "payments_api")
    .AddKeycloakJwtBearer(serviceName: "keycloak", realm: PlatformAuthentication.KeycloakRealm,
        authenticationScheme: PlatformAuthentication.Keycloak, o =>
        {
            o.Audience = "payments-api";
            o.MapInboundClaims = false;
            o.RequireHttpsMetadata = PlatformAuthentication.RequireHttpsMetadata(builder);   // HTTP: Development, inside the cluster
        });
builder.Services.AddPlatformAuthorization();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.MapDefaultEndpoints();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
