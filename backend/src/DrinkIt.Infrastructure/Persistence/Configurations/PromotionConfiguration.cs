using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Promotions;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("Promotions");
        builder.HasAuditColumns();
        builder.HasKey(promotion => promotion.Id);

        builder.Property(promotion => promotion.Name).HasMaxLength(Promotion.NameMaxLength).IsRequired();
        builder.Property(promotion => promotion.DiscountPercent).IsRequired();
        builder.Property(promotion => promotion.StartsAt).IsRequired();
        builder.Property(promotion => promotion.EndsAt).IsRequired();
        builder.Property(promotion => promotion.IsActive).IsRequired();

        // The days are a bit set held in a private field: seven days in one
        // column, no table to join. Days itself is derived.
        builder.Property<int>("_days").HasColumnName("DaysMask").IsRequired();
        builder.Ignore(promotion => promotion.Days);
        builder.Ignore(promotion => promotion.ProductIds);

        // The products it discounts. Their own table, keyed by the promotion so
        // one cannot be listed twice. Products are never deleted, so the
        // foreign key always has a row, and it restricts.
        builder.OwnsMany(promotion => promotion.Products, product =>
        {
            product.ToTable("PromotionProducts");
            product.WithOwner().HasForeignKey("PromotionId");
            product.Property<Guid>("PromotionId");
            product.HasKey("PromotionId", nameof(PromotionProduct.ProductId));
            product.HasOne<Product>().WithMany().HasForeignKey(part => part.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Navigation(promotion => promotion.Products)
            .HasField("_products")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(promotion => promotion.VenueId);

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(promotion => promotion.VenueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
