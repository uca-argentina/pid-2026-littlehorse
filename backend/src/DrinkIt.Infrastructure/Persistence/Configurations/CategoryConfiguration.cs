using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name).HasMaxLength(Category.NameMaxLength).IsRequired();
        builder.Property(category => category.CreatedAt).IsRequired();

        // Composite with VenueId, not global: two venues must be able to have
        // their own "Cervezas". CLAUDE.md, multi-tenancy section.
        builder.HasIndex(category => new { category.VenueId, category.Name }).IsUnique();

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(category => category.VenueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
