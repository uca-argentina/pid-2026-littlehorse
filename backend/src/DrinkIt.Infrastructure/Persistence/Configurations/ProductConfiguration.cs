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
        // A concurrency token, and this is what keeps the last drink from being
        // sold twice: the UPDATE carries the stock it was read at, so of two
        // orders racing for it exactly one matches a row and the other is
        // refused. Found missing in the review of 2026-09-17, with a comment in
        // the domain claiming the opposite.
        builder.Property(product => product.Stock).IsRequired().IsConcurrencyToken();
        builder.Property(product => product.IsAvailable).IsRequired();
        builder.Property(product => product.IsActive).IsRequired();

        // Composite with VenueId, not global: two venues must be able to sell
        // their own "Gin Tonic". CLAUDE.md, multi-tenancy section.
        builder.HasIndex(product => new { product.VenueId, product.Name }).IsUnique();

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(product => product.VenueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
