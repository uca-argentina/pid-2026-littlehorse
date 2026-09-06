using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("Venues");
        builder.HasKey(venue => venue.Id);

        builder.Property(venue => venue.Name).HasMaxLength(100).IsRequired();
        builder.Property(venue => venue.Slug).HasMaxLength(50).IsRequired();

        // Global, not per venue: the slug is the public URL of the whole platform.
        builder.HasIndex(venue => venue.Slug).IsUnique();
    }
}
