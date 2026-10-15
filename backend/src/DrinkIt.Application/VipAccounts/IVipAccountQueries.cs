namespace DrinkIt.Application.VipAccounts;

/// <summary>What a table's QR opens (US-41): which table it is and what it has left to spend.</summary>
public sealed record VipTableAccess(int TableNumber, decimal Balance);

/// <summary>The read side of VIP accounts. No repository: reads have no invariant to protect.</summary>
public interface IVipAccountQueries
{
    /// <summary>
    /// The table the code opens in a night of the current venue underway at
    /// <paramref name="now"/>, or null. Ran out with its night, regenerated,
    /// invented or another venue's all read the same: null.
    /// </summary>
    Task<VipTableAccess?> FindOpenAsync(string accessCode, DateTimeOffset now, CancellationToken cancellationToken);
}
