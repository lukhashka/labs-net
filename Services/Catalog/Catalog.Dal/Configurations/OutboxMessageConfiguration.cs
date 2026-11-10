using Catalog.Dal.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();            // the EventId of the event
        builder.Property(m => m.EventType).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Exchange).IsRequired().HasMaxLength(200);
        builder.Property(m => m.RoutingKey).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.CorrelationId).HasMaxLength(100);
        builder.Property(m => m.TraceParent).HasMaxLength(100);
        builder.Property(m => m.LastError).HasMaxLength(1000);

        // the publisher reads "not yet published, oldest first"; the filter keeps the index small
        builder.HasIndex(m => m.OccurredAt).HasFilter("[PublishedAt] IS NULL");
    }
}
