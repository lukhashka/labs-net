using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Authentication;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Lab 09: the gateway validates the token BEFORE proxying (route policy "default"); /auth is anonymous.
// Audiences are not checked here: every service checks its own audience and permissions again.
builder.AddPlatformAuthentication(identityServerAudience: "orders_api")
    .AddKeycloakJwtBearer(serviceName: "keycloak", realm: PlatformAuthentication.KeycloakRealm,
        authenticationScheme: PlatformAuthentication.Keycloak, o =>
        {
            o.TokenValidationParameters.ValidateAudience = false;
            o.MapInboundClaims = false;
            if (builder.Environment.IsDevelopment())
                o.RequireHttpsMetadata = false;
        });
builder.Services.PostConfigure<JwtBearerOptions>(PlatformAuthentication.IdentityServer,
    o => o.TokenValidationParameters.ValidateAudience = false);
builder.Services.AddAuthorization();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()           // http://orders-api -> real address from Aspire
    .AddTransforms(context => context.AddRequestTransform(transform =>
    {
        // The Authorization header goes further unchanged. X-User-Id is set only from the validated token,
        // a value sent by the client is removed, so it cannot pretend to be another user.
        transform.ProxyRequest.Headers.Remove("X-User-Id");
        if (transform.HttpContext.User.FindFirst("sub")?.Value is { } userId)
            transform.ProxyRequest.Headers.TryAddWithoutValidation("X-User-Id", userId);
        return ValueTask.CompletedTask;
    }));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();           // BEFORE MapReverseProxy: the id goes further in request headers

// Routing decision, result and duration of every request
app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();
    await next();

    // Features.Get instead of GetReverseProxyFeature(): that method throws when the request did not go through a route
    var proxy = context.Features.Get<IReverseProxyFeature>();
    var error = context.GetForwarderErrorFeature();

    app.Logger.LogInformation(
        "{Method} {Path} from {ClientIp} -> route {Route}, cluster {Cluster}: {StatusCode}, {ResponseSize} bytes in {Elapsed} ms",
        context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress,
        proxy?.Route.Config.RouteId, proxy?.Cluster.Config.ClusterId,
        context.Response.StatusCode, context.Response.ContentLength, stopwatch.ElapsedMilliseconds);

    if (error is not null)
        app.Logger.LogError(error.Exception, "Service of cluster {Cluster} is unavailable: {ProxyError}",
            proxy?.Cluster.Config.ClusterId, error.Error);
});

app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();
app.MapReverseProxy();

app.Run();
