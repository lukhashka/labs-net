using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;

namespace Reviews.Application.Catalog.Commands.SyncEventTitle;

public enum SyncResult { Applied, Duplicate, Stale }

// Sent by the consumer of the Catalog events when an event (concert) was renamed
public record SyncEventTitleCommand(Guid MessageId, long CatalogEventId, string Title, DateTime ChangedAt)
    : ICommand<SyncResult>;

// The Catalog is the source of truth, ReviewsDb keeps a copy of the title for its own queries.
//
// Idempotency without a multi-document transaction: the MongoDB container of Aspire is a standalone server,
// transactions need a replica set. So the steps are ordered to be safe on a repeat:
//   1. already processed EventId -> skip;
//   2. an older rename than the one already applied (out of order) -> skip;
//   3. set the title (setting the same value twice changes nothing);
//   4. remember the time of the change and the EventId.
// A crash between 3 and 4 only repeats step 3 on the next delivery.
public class SyncEventTitleCommandHandler(ICatalogReplica replica, ILogger<SyncEventTitleCommandHandler> logger)
    : IRequestHandler<SyncEventTitleCommand, SyncResult>
{
    public async Task<SyncResult> Handle(SyncEventTitleCommand cmd, CancellationToken ct)
    {
        if (await replica.IsProcessedAsync(cmd.MessageId, ct))
        {
            logger.LogInformation("Duplicate rename {EventId} of event {CatalogEventId} skipped", cmd.MessageId, cmd.CatalogEventId);
            return SyncResult.Duplicate;
        }

        var lastApplied = await replica.GetTitleChangedAtAsync(cmd.CatalogEventId, ct);
        if (lastApplied >= cmd.ChangedAt)
        {
            logger.LogWarning("Rename {EventId} of event {CatalogEventId} is older than the applied one ({LastApplied}), skipped",
                cmd.MessageId, cmd.CatalogEventId, lastApplied);
            await replica.MarkProcessedAsync(cmd.MessageId, ct);
            return SyncResult.Stale;
        }

        var (reviews, discussions) = await replica.SetEventTitleAsync(cmd.CatalogEventId, cmd.Title, ct);
        await replica.SaveTitleChangedAtAsync(cmd.CatalogEventId, cmd.ChangedAt, ct);
        await replica.MarkProcessedAsync(cmd.MessageId, ct);

        logger.LogInformation("Event {CatalogEventId} renamed to {Title}: {Reviews} reviews, {Discussions} discussions updated",
            cmd.CatalogEventId, cmd.Title, reviews, discussions);
        return SyncResult.Applied;
    }
}

public class SyncEventTitleCommandValidator : AbstractValidator<SyncEventTitleCommand>
{
    public SyncEventTitleCommandValidator()
    {
        RuleFor(x => x.CatalogEventId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}
