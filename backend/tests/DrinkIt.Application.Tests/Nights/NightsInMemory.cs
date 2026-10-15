using DrinkIt.Application.Nights;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Nights;

/// <summary>
/// The night repository over a list. Like the staff one, it only ever holds one
/// venue's rows: the real one leans on the global query filter for that.
/// </summary>
internal sealed class NightsInMemory(params Night[] stored) : INightRepository
{
    private readonly List<Night> _stored = [.. stored];

    public Night? Added { get; private set; }

    /// <summary>The KDS the added night was opened with, and when (US-37).</summary>
    public IReadOnlyList<StaffUser>? AddedBars { get; private set; }

    public DateTimeOffset? AddedAt { get; private set; }

    /// <summary>The KDS that hold some stock in any night, as the database would answer.</summary>
    public HashSet<Guid> BarsHoldingStock { get; } = [];

    /// <summary>How many times the unit of work was committed.</summary>
    public int Saves { get; private set; }

    public Task<bool> OverlapsAsync(DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excluding, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.Exists(night => night.Id != excluding && night.StartsAt < endsAt && startsAt < night.EndsAt));

    public Task<Night?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.Find(night => night.Id == id));

    /// <summary>Answers the save as the database does when a table's balance moved meanwhile.</summary>
    public bool RefusesTheSave { get; init; }

    public Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (RefusesTheSave) return Task.FromResult(false);

        Saves++;

        return Task.FromResult(true);
    }

    public Task<IReadOnlyCollection<Guid>> BarsHoldingStockAsync(Guid nightId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Guid>>(BarsHoldingStock);

    public Task AddAsync(Night night, IReadOnlyList<StaffUser> bars, DateTimeOffset now, CancellationToken cancellationToken)
    {
        (Added, AddedBars, AddedAt) = (night, bars, now);
        _stored.Add(night);

        return Task.CompletedTask;
    }
}
