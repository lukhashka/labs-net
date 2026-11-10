using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Reviews.WebApi.Metrics;

// Business metrics of the Reviews service. Singleton; the meter is added in ServiceDefaults (AddMeter("Reviews.WebApi")).
// Tags: review.rating (1..5), operation (create-review, update-review, delete-review, add-comment),
// status (success / failure), error.type for failures. No ids of reviews, events or authors.
public sealed class ReviewMetrics
{
    public const string MeterName = "Reviews.WebApi";

    private readonly Counter<long> _created;
    private readonly Counter<long> _comments;
    private readonly Histogram<double> _duration;

    public ReviewMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _created = meter.CreateCounter<long>("reviews.created", unit: "{review}", description: "Reviews written");
        _comments = meter.CreateCounter<long>("reviews.comments.added", unit: "{comment}", description: "Comments added to reviews");
        _duration = meter.CreateHistogram<double>("reviews.operation.duration", unit: "ms", description: "Duration of review operations");
    }

    public void ReviewCreated(int rating) =>
        _created.Add(1, new KeyValuePair<string, object?>("review.rating", rating));

    public void CommentAdded() => _comments.Add(1);

    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action();
            Record(operation, stopwatch, null);
            return result;
        }
        catch (Exception ex)
        {
            Record(operation, stopwatch, ex);
            throw;
        }
    }

    public Task MeasureAsync(string operation, Func<Task> action) =>
        MeasureAsync(operation, async () => { await action(); return true; });

    private void Record(string operation, Stopwatch stopwatch, Exception? error)
    {
        var tags = new TagList { { "operation", operation }, { "status", error is null ? "success" : "failure" } };
        if (error is not null)
            tags.Add("error.type", error.GetType().Name);
        _duration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }
}
