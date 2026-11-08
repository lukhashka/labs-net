using System.Diagnostics;
using Platform.ServiceDefaults;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();          // http://orders-api -> real address from Aspire

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
app.MapReverseProxy();

app.Run();
