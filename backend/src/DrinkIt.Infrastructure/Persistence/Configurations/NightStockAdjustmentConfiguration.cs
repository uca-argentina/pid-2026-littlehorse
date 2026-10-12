using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class NightStockAdjustmentConfiguration : IEntityTypeConfiguration<NightStockAdjustment>
{
    public void Configure(EntityTypeBuilder<NightStockAdjustment> builder)
    {
        builder.ToTable("NightStockAdjustments");
        builder.HasAuditColumns();
        builder.HasKey(adjustment => adjustment.Id);

        builder.Property(adjustment => adjustment.Change).IsRequired();

        // The night's stock sheet reads every adjustment of a night, by product.
        builder.HasIndex(adjustment => new { adjustment.VenueId, adjustment.NightId, adjustment.ProductId });

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.VenueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Night>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.NightId)
            .OnDelete(DeleteBehavior.Restrict);

        // Products are never hard-deleted (IsActive), so the key always holds.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
