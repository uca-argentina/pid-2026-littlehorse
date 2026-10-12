using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class BarStockConfiguration : IEntityTypeConfiguration<BarStock>
{
    public void Configure(EntityTypeBuilder<BarStock> builder)
    {
        builder.ToTable("BarStocks", table =>
            table.HasCheckConstraint("CK_BarStocks_Units", "[Units] >= 0"));

        // One row per night, bar and product: the key is what stops two first
        // loads of the same drink into the same bar from making two rows.
        builder.HasKey(stock => new { stock.NightId, stock.KdsId, stock.ProductId });

        builder.Property(stock => stock.Units).IsRequired();

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(stock => stock.VenueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Night>()
            .WithMany()
            .HasForeignKey(stock => stock.NightId)
            .OnDelete(DeleteBehavior.Restrict);

        // Staff and products are never hard-deleted (IsActive), so the keys hold.
        builder.HasOne<StaffUser>()
            .WithMany()
            .HasForeignKey(stock => stock.KdsId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(stock => stock.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
