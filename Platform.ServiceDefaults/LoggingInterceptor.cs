using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Platform.ServiceDefaults;

// Common log of gRPC calls on the server. Protocol=grpc in LogContext separates these calls from REST in the logs,
// including cache operations that happen inside the call.
public class LoggingInterceptor(ILogger<LoggingInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var stopwatch = Stopwatch.StartNew();
        var correlationId = context.RequestHeaders.GetValue(CorrelationIdMiddleware.HeaderName);
        Activity.Current?.SetTag("rpc.metadata.x-correlation-id", correlationId);

        using (LogContext.PushProperty("Protocol", "grpc"))
        {
            try
            {
                var response = await continuation(request, context);
                logger.LogInformation("gRPC {Method} from {Peer}: {Status} in {Elapsed} ms",
                    context.Method, context.Peer, StatusCode.OK, stopwatch.ElapsedMilliseconds);
                return response;
            }
            catch (RpcException ex)
            {
                logger.LogWarning(ex, "gRPC {Method} from {Peer}: {Status} in {Elapsed} ms",
                    context.Method, context.Peer, ex.StatusCode, stopwatch.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "gRPC {Method} from {Peer} failed in {Elapsed} ms",
                    context.Method, context.Peer, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
