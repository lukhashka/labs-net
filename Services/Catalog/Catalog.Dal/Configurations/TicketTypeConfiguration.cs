using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public class TicketTypeConfiguration : IEntityTypeConfiguration<TicketType>
{
    public void Configure(EntityTypeBuilder<TicketType> builder)
    {
        builder.ToTable("TicketTypes", t =>
        {
            t.HasCheckConstraint("CK_TicketTypes_Price", "[Price] > 0");
            t.HasCheckConstraint("CK_TicketTypes_Stock", "[AvailableStock] >= 0 AND [AvailableStock] <= [Capacity]");
        });
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(50);
        builder.Property(t => t.Code).IsRequired().HasMaxLength(50);
        builder.Property(t => t.Price).HasColumnType("decimal(18,2)");

        builder.HasIndex(t => t.Code).IsUnique();                    // Code ~ SKU
        builder.HasIndex(t => new { t.EventId, t.Price });           // ticket types of an event ordered by price

        builder.HasQueryFilter(t => !t.Event!.IsDeleted);
    }
}
