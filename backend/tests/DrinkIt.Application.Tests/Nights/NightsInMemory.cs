using DrinkIt.Application.Nights;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Tests.Nights;

/// <summary>
/// The night repository over a list. Like the staff one, it only ever holds one
/// venue's rows: the real one leans on the global query filter for that.
/// </summary>
internal sealed class NightsInMemory(params Night[] stored) : INightRepository
{
    private readonly List<Night> _stored = [.. stored];

    public Night? Added { get; private set; }

    /// <summary>How many times the unit of work was committed.</summary>
    public int Saves { get; private set; }

    public Task<bool> OverlapsAsync(DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excluding, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.Exists(night => night.Id != excluding && night.StartsAt < endsAt && startsAt < night.EndsAt));

    public Task<Night?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.Find(night => night.Id == id));

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;

        return Task.CompletedTask;
    }

    public Task AddAsync(Night night, CancellationToken cancellationToken)
    {
        Added = night;
        _stored.Add(night);

        return Task.CompletedTask;
    }
}
