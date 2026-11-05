using DrinkIt.Domain.Menu;
using DrinkIt.Domain.StampCards;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

internal sealed class StampCardRuleConfiguration : IEntityTypeConfiguration<StampCardRule>
{
    public void Configure(EntityTypeBuilder<StampCardRule> builder)
    {
        builder.ToTable("StampCardRules");
        builder.HasAuditColumns();
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.StampsForReward).IsRequired();
        builder.Property(rule => rule.RewardProductId).IsRequired();
        builder.Property(rule => rule.IsActive).IsRequired();

        // One per venue (US-57): the index is what makes "the rule" a thing.
        builder.HasIndex(rule => rule.VenueId).IsUnique();

        // Products are never deleted, so the foreign key always has a row.
        builder.HasOne<Product>().WithMany().HasForeignKey(rule => rule.RewardProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(rule => rule.VenueId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StampCardConfiguration : IEntityTypeConfiguration<StampCard>
{
    /// <summary>The concurrency token, kept out of the domain: two phones holding one card spend it once (US-59).</summary>
    internal const string Version = "Version";

    public void Configure(EntityTypeBuilder<StampCard> builder)
    {
        builder.ToTable("StampCards");
        builder.HasCreationTime();
        builder.HasKey(card => card.Id);

        builder.Property(card => card.Token).HasMaxLength(StampCard.TokenLength).IsFixedLength().IsRequired();
        builder.Property(card => card.RewardsClaimed).IsRequired();
        builder.Property<byte[]>(Version).IsRowVersion();

        // Composite with VenueId (CLAUDE.md): a token of another venue finds nothing.
        builder.HasIndex(card => new { card.VenueId, card.Token }).IsUnique();

        builder.HasOne<Venue>().WithMany().HasForeignKey(card => card.VenueId).OnDelete(DeleteBehavior.Restrict);
    }
}
