using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public class EventCategoryConfiguration : IEntityTypeConfiguration<EventCategory>
{
    public void Configure(EntityTypeBuilder<EventCategory> builder)
    {
        builder.ToTable("EventCategories");
        builder.HasKey(ec => new { ec.EventId, ec.CategoryId });     // composite key
        builder.Property(ec => ec.AddedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(ec => new { ec.CategoryId, ec.AddedAt });   // "recently added to category"

        builder.HasOne(ec => ec.Event).WithMany(e => e.EventCategories)
            .HasForeignKey(ec => ec.EventId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ec => ec.Category).WithMany(c => c.EventCategories)
            .HasForeignKey(ec => ec.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // both sides have query filters only on Event, so hide join rows of deleted events too
        builder.HasQueryFilter(ec => !ec.Event.IsDeleted);
    }
}
