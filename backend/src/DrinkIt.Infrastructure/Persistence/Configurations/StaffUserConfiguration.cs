using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> builder)
    {
        builder.ToTable("StaffUsers");
        builder.HasAuditColumns();
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Username).HasMaxLength(50).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(user => user.Role).IsRequired();
        builder.Property(user => user.IsActive).IsRequired();

        // Composite with VenueId, not global: two venues must be able to have
        // their own "euge". CLAUDE.md, multi-tenancy section.
        builder.HasIndex(user => new { user.VenueId, user.Username }).IsUnique();

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(user => user.VenueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
