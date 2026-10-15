using DrinkIt.Domain.Tables;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class TableConfiguration : IEntityTypeConfiguration<Table>
{
    public void Configure(EntityTypeBuilder<Table> builder)
    {
        builder.ToTable("Tables");
        builder.HasAuditColumns();
        builder.HasKey(table => table.Id);
        builder.Property(table => table.Location).HasMaxLength(Table.LocationMaxLength);

        // Composite with VenueId, not global: two venues must be able to have
        // their own "Mesa 5". It holds for put-away tables too, on purpose: the
        // number stays taken, so old orders never point at another table.
        builder.HasIndex(table => new { table.VenueId, table.Number }).IsUnique();

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(table => table.VenueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
