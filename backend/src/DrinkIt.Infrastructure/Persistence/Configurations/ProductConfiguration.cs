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
        builder.HasKey(product => product.Id);

        builder.Property(product => product.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(product => product.Description).HasMaxLength(Product.DescriptionMaxLength);
        // Long enough for a blob address with a SAS token, should one ever be needed.
        builder.Property(product => product.ImageUrl).HasMaxLength(2048);
        // Pesos with cents. Ten digits leaves room for a bottle at eight figures.
        builder.Property(product => product.Price).HasPrecision(10, 2).IsRequired();
        // Not a concurrency token, and that was tried: it put Stock in the
        // WHERE of every UPDATE of a product, so uploading a photo while the
        // bar sold that drink failed with a 500 and lost the photo. What keeps
        // the last drink from being sold twice is the conditional update in
        // OrderRepository, which only the sale performs.
        builder.Property(product => product.Stock).IsRequired();
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
