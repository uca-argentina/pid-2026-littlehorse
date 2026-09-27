using DrinkIt.Domain.Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrinkIt.Infrastructure.Persistence.Configurations;

/// <summary>
/// The audit columns of US-30, mapped in one place so every table that has them
/// has the same ones, with the same names and the same limits.
/// </summary>
internal static class AuditColumns
{
    /// <summary>Same limit as a username, which is what gets stored.</summary>
    private const int ByMaxLength = 50;

    /// <summary>
    /// Nullable on purpose: a row that existed before the column has no date,
    /// and inventing one would say when the migration ran, not when it happened.
    /// </summary>
    public static EntityTypeBuilder<T> HasCreationTime<T>(this EntityTypeBuilder<T> builder)
        where T : class, IHasCreationTime
    {
        builder.Property<DateTimeOffset?>(nameof(IHasCreationTime.CreatedAt));

        return builder;
    }

    public static EntityTypeBuilder<T> HasAuditColumns<T>(this EntityTypeBuilder<T> builder)
        where T : class, IAudited
    {
        builder.HasCreationTime();
        builder.Property<string?>(nameof(IAudited.CreatedBy)).HasMaxLength(ByMaxLength);
        builder.Property<DateTimeOffset?>(nameof(IAudited.LastModifiedAt));
        builder.Property<string?>(nameof(IAudited.LastModifiedBy)).HasMaxLength(ByMaxLength);

        return builder;
    }
}
