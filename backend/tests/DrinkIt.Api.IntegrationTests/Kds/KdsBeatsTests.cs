using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Kds;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Kds;

/// <summary>
/// US-38: each bar's tablet beats, the last beat is kept in the database —
/// the API runs up to two replicas and scales to zero, so what one of them
/// holds in memory is not enough — and paying reads it with the bars.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class KdsBeatsTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Saturday = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public async Task GetAsync_AfterATabletBeats_CarriesWhenItLastBeat()
    {
        Seeded seeded = await Seed();

        await Beat(seeded.Venue, seeded.MainBar, Saturday.AddMinutes(10));

        IReadOnlyList<BarSupply> bars = await BarsOf(seeded);

        Assert.Equal(Saturday.AddMinutes(10), bars.Single(bar => bar.KdsId == seeded.MainBar.Id).LastBeatAt);
    }

    [Fact]
    public async Task GetAsync_ForATabletThatNeverBeat_CarriesNoBeat()
    {
        Seeded seeded = await Seed();

        await Beat(seeded.Venue, seeded.MainBar, Saturday.AddMinutes(10));

        IReadOnlyList<BarSupply> bars = await BarsOf(seeded);

        Assert.Null(bars.Single(bar => bar.KdsId == seeded.VipBar.Id).LastBeatAt);
    }

    // One row per bar, moved forward: the second beat is not a second row.
    [Fact]
    public async Task RecordAsync_WhenTheSameTabletBeatsAgain_KeepsOnlyTheLatest()
    {
        Seeded seeded = await Seed();

        await Beat(seeded.Venue, seeded.MainBar, Saturday.AddMinutes(10));
        await Beat(seeded.Venue, seeded.MainBar, Saturday.AddMinutes(11));

        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);
        List<DateTimeOffset> beats = await read.KdsBeats
            .Where(beat => beat.KdsId == seeded.MainBar.Id)
            .Select(beat => beat.LastBeatAt)
            .ToListAsync();

        Assert.Equal([Saturday.AddMinutes(11)], beats);
    }

    // A tablet that opens two connections at once — the board in two tabs —
    // makes two first beats in the same instant. Neither can fail the other.
    [Fact]
    public async Task RecordAsync_WhenTwoFirstBeatsArriveTogether_KeepsOneRow()
    {
        Seeded seeded = await Seed();

        await Task.WhenAll(
            Beat(seeded.Venue, seeded.MainBar, Saturday.AddMinutes(10)),
            Beat(seeded.Venue, seeded.MainBar, Saturday.AddMinutes(10)));

        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);

        Assert.Equal(1, await read.KdsBeats.CountAsync(beat => beat.KdsId == seeded.MainBar.Id));
    }

    // CLAUDE.md's invariant, for this table too.
    [Fact]
    public async Task KdsBeats_FromAnotherVenue_AreNeverVisible()
    {
        Seeded seeded = await Seed();

        await Beat(seeded.Venue, seeded.MainBar, Saturday.AddMinutes(10));

        await using DrinkItDbContext theirs = sql.CreateContext(seeded.Theirs.Id);

        Assert.False(await theirs.KdsBeats.AnyAsync());
    }

    private async Task Beat(Venue venue, StaffUser bar, DateTimeOffset at)
    {
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        await new KdsBeats(context, new FixedVenue(venue.Id)).RecordAsync(bar.Id, at, CancellationToken.None);
    }

    private async Task<IReadOnlyList<BarSupply>> BarsOf(Seeded seeded)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await new BarsForOrdering(context).GetAsync(seeded.Saturday.Id, [], CancellationToken.None);
    }

    private sealed record Seeded(Venue Venue, Venue Theirs, StaffUser MainBar, StaffUser VipBar, Night Saturday);

    private async Task<Seeded> Seed()
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");
        StaffUser mainBar = StaffUser.Create(mine.Id, "main-bar", "hash", StaffRole.Kds);
        StaffUser vipBar = StaffUser.Create(mine.Id, "vip-bar", "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(mine.Id, "till-1", "hash", StaffRole.Cashier);
        Night saturday = Night.Create(mine.Id, "Saturday", Saturday, Saturday.AddHours(7), [mainBar, vipBar, till]);

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.StaffUsers.AddRange(mainBar, vipBar, till);
        seed.Nights.Add(saturday);
        await seed.SaveChangesAsync();

        return new Seeded(mine, theirs, mainBar, vipBar, saturday);
    }

    private sealed class FixedVenue(Guid id) : ICurrentVenue
    {
        public Guid Id => id;
    }
}
