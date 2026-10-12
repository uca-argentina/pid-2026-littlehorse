using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Persistence;

/// <summary>
/// The storeroom (US-37): a product's general stock minus what the bars of the
/// nights not over yet hold. Never stored, so it cannot disagree with either:
/// what a bar did not sell is back in it the moment its night ends.
/// </summary>
/// <remarks>
/// Whatever checks the storeroom before writing locks the product's row first,
/// and reads in a statement of its own after that: the database reads a
/// committed snapshot per statement (READ_COMMITTED_SNAPSHOT is on, as in Azure
/// SQL), so a check made in the same statement as the lock could see what was
/// there before the lock was granted.
/// </remarks>
internal static class Storeroom
{
    /// <summary>
    /// Locks the product's row until the transaction ends, and returns its
    /// general stock as it is once the lock is held. Raw SQL, so no venue
    /// filter: the caller already found the product through the filtered set.
    /// </summary>
    public static Task<int> LockProductAsync(DrinkItDbContext context, Guid productId, CancellationToken cancellationToken) =>
        context.Database
            .SqlQuery<int>($"SELECT [Stock] AS [Value] FROM [Products] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {productId}")
            .SingleAsync(cancellationToken);

    /// <summary>
    /// Locks the rows of every active product of the venue until the
    /// transaction ends. Raw SQL, so the venue is written out: no filter
    /// reaches here.
    /// </summary>
    public static Task LockActiveProductsAsync(DrinkItDbContext context, Guid venueId, CancellationToken cancellationToken) =>
        context.Database
            .SqlQuery<Guid>($"SELECT [Id] AS [Value] FROM [Products] WITH (UPDLOCK, ROWLOCK) WHERE [VenueId] = {venueId} AND [IsActive] = CAST(1 AS bit)")
            .ToListAsync(cancellationToken);

    /// <summary>What the bars of every night not over at <paramref name="now"/> hold, product by product.</summary>
    public static Task<Dictionary<Guid, int>> HeldInBarsAsync(DrinkItDbContext context, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.BarStocks
            .Where(stock => context.Nights.Any(night => night.Id == stock.NightId && night.EndsAt > now))
            .GroupBy(stock => stock.ProductId)
            .Select(group => new { ProductId = group.Key, Units = group.Sum(stock => stock.Units) })
            .ToDictionaryAsync(held => held.ProductId, held => held.Units, cancellationToken);

    /// <summary>What the bars of every night not over at <paramref name="now"/> hold of the product, upcoming nights included.</summary>
    public static Task<int> HeldInBarsAsync(DrinkItDbContext context, Guid productId, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.BarStocks
            .Where(stock => stock.ProductId == productId && context.Nights.Any(night => night.Id == stock.NightId && night.EndsAt > now))
            .SumAsync(stock => stock.Units, cancellationToken);
}
