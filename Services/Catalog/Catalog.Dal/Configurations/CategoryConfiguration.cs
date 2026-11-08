using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(c => c.Name).IsUnique();

        // static seed (HasData) — reference data with fixed Ids
        builder.HasData(
            new { Id = 1, Name = "Концерт" },
            new { Id = 2, Name = "Хакатон" },
            new { Id = 3, Name = "Лекція" },
            new { Id = 4, Name = "Спорт" },
            new { Id = 5, Name = "Вечірка" },
            new { Id = 6, Name = "Воркшоп" });
    }
}
