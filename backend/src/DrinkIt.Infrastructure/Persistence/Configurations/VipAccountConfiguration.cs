using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.Venues;
using DrinkIt.Domain.VipAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class VipAccountConfiguration : IEntityTypeConfiguration<VipAccount>
{
    /// <summary>The concurrency token, kept out of the domain: two phones of one table spend the same balance (US-41).</summary>
    internal const string Version = "Version";

    public void Configure(EntityTypeBuilder<VipAccount> builder)
    {
        builder.ToTable("VipAccounts");
        builder.HasAuditColumns();
        builder.HasKey(account => account.Id);

        builder.Property(account => account.Budget).HasPrecision(10, 2).IsRequired();
        builder.Property(account => account.Spent).HasPrecision(10, 2).IsRequired();
        builder.Ignore(account => account.Balance);
        builder.Property(account => account.AccessCode).HasMaxLength(VipAccount.AccessCodeLength).IsFixedLength().IsRequired();
        builder.Property<byte[]>(Version).IsRowVersion();

        // A table is in a night once, with one budget.
        builder.HasIndex(account => new { account.VenueId, account.NightId, account.TableId }).IsUnique();

        // The QR is looked up by its code alone (US-41), before any venue is known.
        builder.HasIndex(account => account.AccessCode).IsUnique();

        builder.HasOne<Venue>().WithMany().HasForeignKey(account => account.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Night>().WithMany().HasForeignKey(account => account.NightId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Table>().WithMany().HasForeignKey(account => account.TableId).OnDelete(DeleteBehavior.Restrict);
    }
}
