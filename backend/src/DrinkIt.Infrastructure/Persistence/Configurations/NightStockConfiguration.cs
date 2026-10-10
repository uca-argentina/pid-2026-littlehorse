using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class NightStockConfiguration : IEntityTypeConfiguration<NightStock>
{
    public void Configure(EntityTypeBuilder<NightStock> builder)
    {
        builder.ToTable("NightStocks");
        // Who adjusted what a night loaded, and when. A sale moves Remaining
        // with its own statement and is not an edit, so it leaves these alone.
        builder.HasAuditColumns();
        builder.HasKey(stock => stock.Id);

        builder.Property(stock => stock.NightId).IsRequired();
        builder.Property(stock => stock.ProductId).IsRequired();
        // Not concurrency tokens, for the reason Product.Stock never was: a
        // sale moves Remaining with one relative statement in OrderRepository,
        // and a token would put it in the WHERE of every edit of the row.
        builder.Property(stock => stock.Loaded).IsRequired();
        builder.Property(stock => stock.Remaining).IsRequired();

        // Derived, never stored: both can only disagree with what they come from.
        builder.Ignore(stock => stock.Sold);
        builder.Ignore(stock => stock.IsSoldOut);

        // One row per product per night, composite with VenueId like every
        // other unique key. The sale looks the row up by night and product.
        builder.HasIndex(stock => new { stock.VenueId, stock.NightId, stock.ProductId }).IsUnique();

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(stock => stock.VenueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Night>()
            .WithMany()
            .HasForeignKey(stock => stock.NightId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(stock => stock.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
