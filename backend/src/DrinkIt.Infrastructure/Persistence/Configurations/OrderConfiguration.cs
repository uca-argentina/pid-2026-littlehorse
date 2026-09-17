using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <summary>
    /// The column that tells one attempt at the same order from another. A
    /// shadow property because it is how the request arrived and not something
    /// true about the drinks: the domain never sees it. See IOrderRepository.
    /// </summary>
    public const string IdempotencyKey = "IdempotencyKey";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(order => order.Id);

        builder.Property(order => order.CustomerName).HasMaxLength(Order.CustomerNameMaxLength).IsRequired();
        builder.Property(order => order.Status).IsRequired();
        builder.Property(order => order.PaidAt);

        // Stored as the six characters the customer reads, not as two columns:
        // it is one thing, and every query looks it up whole.
        builder.Property(order => order.Code)
            .HasConversion(code => code.Value, value => OrderCode.Parse(value))
            .HasMaxLength(6)
            .IsRequired();

        builder.Property<string>(IdempotencyKey).HasMaxLength(100).IsRequired();

        // Both composite with VenueId: codes run per venue, and so do the keys.
        // Two venues can hand out K-4821 the same night without meeting.
        builder.HasIndex(order => new { order.VenueId, order.Code }).IsUnique();
        builder.HasIndex(IdempotencyKey, nameof(Order.VenueId)).IsUnique();

        // Derived from the lines, never stored: a stored total is one that can
        // disagree with what it is a total of.
        builder.Ignore(order => order.Total);

        // The lines are the order's own and reachable only through it, which is
        // what makes this an aggregate: they load with it and die with it.
        builder.HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.Items)
            .HasField("_items")
            .UsePropertyAccessMode(Microsoft.EntityFrameworkCore.PropertyAccessMode.Field);

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(order => order.VenueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.ProductId).IsRequired();

        // Copied in rather than read back through ProductId: a ticket has to
        // say what was charged the night it was charged, and the menu moves.
        builder.Property(item => item.ProductName).HasMaxLength(Domain.Menu.Product.NameMaxLength).IsRequired();
        builder.Property(item => item.UnitPrice).HasPrecision(10, 2).IsRequired();
        builder.Property(item => item.Quantity).IsRequired();
        builder.Property(item => item.Note).HasMaxLength(OrderItem.NoteMaxLength);

        builder.Ignore(item => item.Total);
    }
}
