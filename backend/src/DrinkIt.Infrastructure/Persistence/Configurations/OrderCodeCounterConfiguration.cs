using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class OrderCodeCounterConfiguration : IEntityTypeConfiguration<OrderCodeCounter>
{
    public void Configure(EntityTypeBuilder<OrderCodeCounter> builder)
    {
        builder.ToTable("OrderCodeCounters");

        // The venue is the key: one counter each, and the primary key is what
        // makes two requests unable to start the same venue off twice.
        builder.HasKey(counter => counter.VenueId);

        builder.Property(counter => counter.LastCode).HasMaxLength(6).IsRequired();
    }
}
