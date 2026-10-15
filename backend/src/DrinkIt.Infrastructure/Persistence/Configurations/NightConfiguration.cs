using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class NightConfiguration : IEntityTypeConfiguration<Night>
{
    public void Configure(EntityTypeBuilder<Night> builder)
    {
        builder.ToTable("Nights");
        builder.HasAuditColumns();
        builder.HasKey(night => night.Id);

        builder.Property(night => night.Name).HasMaxLength(Night.NameMaxLength).IsRequired();
        builder.Property(night => night.StartsAt).IsRequired();
        builder.Property(night => night.EndsAt).IsRequired();

        // A JSON column and not a join table: the crew is a handful of ids that
        // load and save with the night, staff are never hard-deleted so there is
        // no foreign key to keep, and "is this account in tonight's crew" still
        // translates to SQL through OPENJSON.
        builder.Ignore(night => night.CrewIds);
        builder.PrimitiveCollection<List<Guid>>("_crewIds")
            .HasColumnName("CrewIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        // Every night lookup is "which one of this venue covers this moment".
        builder.HasIndex(night => new { night.VenueId, night.StartsAt, night.EndsAt });

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(night => night.VenueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
