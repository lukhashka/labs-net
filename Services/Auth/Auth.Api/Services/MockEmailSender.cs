using System.Collections.Concurrent;

namespace Auth.Api.Services;

public sealed record EmailMessage(string To, string Subject, string Body, DateTime SentAt);

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

// Development only: letters are not sent, they are logged and kept in memory.
// GET /auth/dev/mailbox?email=... (mapped only in Development) shows them, so the flows can be tested end to end.
public sealed class MockEmailSender(ILogger<MockEmailSender> logger) : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        _sent.Enqueue(new EmailMessage(to, subject, body, DateTime.UtcNow));
        while (_sent.Count > 100 && _sent.TryDequeue(out _)) { }
        logger.LogInformation("Mock e-mail to {To}: {Subject}", to, subject);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> For(string email) =>
        _sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).Reverse().ToList();
}
