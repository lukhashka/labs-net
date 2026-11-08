using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("Venues", t => t.HasCheckConstraint("CK_Venues_Capacity", "[Capacity] > 0"));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).IsRequired().HasMaxLength(150);
        builder.Property(v => v.Address).IsRequired().HasMaxLength(300);
        builder.HasIndex(v => v.Name).IsUnique();

        builder.HasMany(v => v.Events)                              // 1:N
            .WithOne(e => e.Venue)
            .HasForeignKey(e => e.VenueId)
            .OnDelete(DeleteBehavior.Restrict);                     // venue with events can't be deleted

        // static seed of reference data with fixed Ids
        builder.HasData(
            new { Id = 1, Name = "Актова зала ЧНУ (корпус 1)", Address = "вул. Коцюбинського, 2", Capacity = 400 },
            new { Id = 2, Name = "Спорткомплекс ЧНУ", Address = "вул. Університетська, 28", Capacity = 1200 },
            new { Id = 3, Name = "Коворкінг ФМІ", Address = "вул. Університетська, 28, ауд. 25", Capacity = 80 });
    }
}
