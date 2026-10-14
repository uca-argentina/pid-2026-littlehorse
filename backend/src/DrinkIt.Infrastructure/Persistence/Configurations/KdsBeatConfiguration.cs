using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class KdsBeatConfiguration : IEntityTypeConfiguration<KdsBeat>
{
    public void Configure(EntityTypeBuilder<KdsBeat> builder)
    {
        builder.ToTable("KdsBeats");

        // One row per tablet: the key is what stops two first beats from
        // making two rows.
        builder.HasKey(beat => beat.KdsId);

        builder.Property(beat => beat.LastBeatAt).IsRequired();

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(beat => beat.VenueId)
            .OnDelete(DeleteBehavior.Restrict);

        // Staff are never hard-deleted (IsActive), so the key holds.
        builder.HasOne<StaffUser>()
            .WithMany()
            .HasForeignKey(beat => beat.KdsId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
