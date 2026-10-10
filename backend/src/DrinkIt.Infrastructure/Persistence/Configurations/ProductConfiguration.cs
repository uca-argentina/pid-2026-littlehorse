using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasAuditColumns();
        builder.HasKey(product => product.Id);

        builder.Property(product => product.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(product => product.Description).HasMaxLength(Product.DescriptionMaxLength);
        // Long enough for a blob address with a SAS token, should one ever be needed.
        builder.Property(product => product.ImageUrl).HasMaxLength(2048);
        // Pesos with cents. Ten digits leaves room for a bottle at eight figures.
        builder.Property(product => product.Price).HasPrecision(10, 2).IsRequired();
        // The column kept its name when the stock moved to the night (US-37):
        // it is the same number, and is now where the first night starts from.
        builder.Property(product => product.InitialStock).HasColumnName("Stock").IsRequired();
        builder.Property(product => product.CategoryId).IsRequired();
        builder.Property(product => product.IsAvailable).IsRequired();
        builder.Property(product => product.IsActive).IsRequired();

        // Composite with VenueId, not global: two venues must be able to sell
        // their own "Gin Tonic". CLAUDE.md, multi-tenancy section.
        builder.HasIndex(product => new { product.VenueId, product.Name }).IsUnique();

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(product => product.VenueId)
            .OnDelete(DeleteBehavior.Restrict);

        // A category with products in it cannot go: nothing deletes them today,
        // and the day something does, it has to decide what happens to the
        // products first.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
