using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public class EventDetailConfiguration : IEntityTypeConfiguration<EventDetail>
{
    public void Configure(EntityTypeBuilder<EventDetail> builder)
    {
        builder.ToTable("EventDetails", t => t.HasCheckConstraint("CK_EventDetails_AgeLimit", "[AgeLimit] BETWEEN 0 AND 21"));
        builder.HasKey(d => d.EventId);                              // PK = FK (1:1)
        builder.Property(d => d.Description).IsRequired().HasMaxLength(4000);
        builder.Property(d => d.Organizer).HasMaxLength(200);
        builder.Property(d => d.AgeLimit).HasDefaultValue(0);
    }
}
