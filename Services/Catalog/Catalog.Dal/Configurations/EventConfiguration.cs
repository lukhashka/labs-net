using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events", t => t.HasCheckConstraint("CK_Events_Dates", "[EndsAt] > [StartsAt]"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Slug).IsRequired().HasMaxLength(200);
        builder.Property(e => e.CoverUrl).HasMaxLength(500);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(EventStatus.Draft);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.IsDeleted).HasDefaultValue(false);
        builder.Property(e => e.RowVersion).IsRowVersion();          // optimistic concurrency

        builder.HasIndex(e => e.Slug).IsUnique();
        builder.HasIndex(e => e.StartsAt);                           // list of upcoming events, sort by date
        builder.HasIndex(e => new { e.VenueId, e.StartsAt });        // events of a venue

        builder.HasQueryFilter(e => !e.IsDeleted);                   // soft delete

        builder.HasOne(e => e.Detail)                                // 1:1
            .WithOne()
            .HasForeignKey<EventDetail>(d => d.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.TicketTypes)                          // 1:N
            .WithOne(t => t.Event)
            .HasForeignKey(t => t.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
