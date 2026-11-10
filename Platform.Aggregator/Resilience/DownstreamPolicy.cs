namespace Platform.Aggregator.Resilience;

// Resilience settings of one downstream service. Why these values: README, section "Resilience (Lab 07)".
//
// Three layers, from the outside in:
//   1. CallTimeout      - gRPC deadline of the whole call, all retries included; nothing waits longer.
//   2. gRPC RetryPolicy - repeats statuses a running server returns (Unavailable, ResourceExhausted).
//   3. Polly handler    - one gRPC attempt = one HTTP/2 request: attempt timeout, network retries, circuit breaker.
// The deadline caps the total, so retries of layers 2 and 3 cannot add up to a long wait.
//
// Timeouts must agree: all Polly attempts of one gRPC attempt fit into CallTimeout. Otherwise the last attempt is
// cancelled by the deadline, the circuit breaker does not count a cancellation as a failure and never opens.
// EnsureConsistent checks it at start.
public sealed record DownstreamPolicy(
    string Service,
    bool Critical,
    TimeSpan CallTimeout,
    int GrpcMaxAttempts,             // gRPC counts the first attempt too: 4 = 1 call + 3 retries
    TimeSpan GrpcInitialBackoff,
    int HttpRetries,                 // network failures inside one gRPC attempt (connection refused, reset)
    TimeSpan HttpRetryDelay,
    TimeSpan AttemptTimeout,
    double FailureRatio,
    int MinimumThroughput,
    TimeSpan BreakDuration)
{
    // Critical: without the order there is nothing to show (503). More attempts, the breaker opens earlier.
    public static readonly DownstreamPolicy Orders = new(
        "orders-api", Critical: true,
        CallTimeout: TimeSpan.FromSeconds(6),
        GrpcMaxAttempts: 4, GrpcInitialBackoff: TimeSpan.FromMilliseconds(300),
        HttpRetries: 2, HttpRetryDelay: TimeSpan.FromMilliseconds(200),
        AttemptTimeout: TimeSpan.FromSeconds(1.5),     // the first read after a Redis failure waits for its 1 s timeout
        FailureRatio: 0.3, MinimumThroughput: 10, BreakDuration: TimeSpan.FromSeconds(15));

    // Critical: an order card without events and an event page without the event lose their main content
    public static readonly DownstreamPolicy Catalog = Orders with { Service = "catalog-api" };

    // Not critical: the answer is still useful without ratings (partial answer of Lab 05).
    // Gives up fast, tolerates more failures before opening and gives the service more time to recover.
    public static readonly DownstreamPolicy Reviews = new(
        "reviews-api", Critical: false,
        CallTimeout: TimeSpan.FromSeconds(2),
        GrpcMaxAttempts: 2, GrpcInitialBackoff: TimeSpan.FromMilliseconds(200),
        HttpRetries: 1, HttpRetryDelay: TimeSpan.FromMilliseconds(100),
        AttemptTimeout: TimeSpan.FromMilliseconds(500),
        FailureRatio: 0.7, MinimumThroughput: 10, BreakDuration: TimeSpan.FromSeconds(30));

    public DateTime Deadline() => DateTime.UtcNow.Add(CallTimeout);

    // Worst time of one gRPC attempt in Polly: every HTTP attempt times out, exponential delays between them
    public TimeSpan PollyBudget()
    {
        var total = AttemptTimeout * (HttpRetries + 1);
        for (var i = 0; i < HttpRetries; i++)
            total += HttpRetryDelay * Math.Pow(2, i);
        return total;
    }

    public void EnsureConsistent()
    {
        if (PollyBudget() >= CallTimeout)
            throw new InvalidOperationException(
                $"{Service}: Polly needs up to {PollyBudget().TotalSeconds} s, the call deadline is {CallTimeout.TotalSeconds} s");
    }
}
