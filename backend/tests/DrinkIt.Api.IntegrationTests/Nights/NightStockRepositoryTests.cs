using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Nights;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>
/// US-37: what the stock of a night reads and writes. Each question is asked
/// of the venue of the request and of no other.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NightStockRepositoryTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 9, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Saturday = Friday.AddDays(1);
    private static readonly DateTimeOffset Sunday = Friday.AddDays(2);

    [Fact]
    public async Task FindNightAsync_WhenItIsTheVenuesFirstNight_HasNoPreviousEnd()
    {
        Scenario s = await AVenueWithNights(Friday);

        NightForStock? found = await Repository(s.Venue).FindNightAsync(s.Nights[0].Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(Friday, found.StartsAt);
        Assert.Null(found.PreviousEndedAt);
    }

    // Two earlier nights: the one that matters is the latest, not the oldest.
    [Fact]
    public async Task FindNightAsync_WhenEarlierNightsExist_ReportsWhenTheLatestOneEnded()
    {
        Scenario s = await AVenueWithNights(Friday, Saturday, Sunday);

        NightForStock? found = await Repository(s.Venue).FindNightAsync(s.Nights[2].Id, CancellationToken.None);

        Assert.Equal(s.Nights[1].EndsAt, found!.PreviousEndedAt);
    }

    [Fact]
    public async Task FindNightAsync_WhenTheNightIsAnotherVenues_FindsNothing()
    {
        Scenario mine = await AVenueWithNights(Friday);
        Scenario theirs = await AVenueWithNights(Friday);

        Assert.Null(await Repository(mine.Venue).FindNightAsync(theirs.Nights[0].Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListActiveProductsAsync_WhenSomeAreDeactivatedOrAnotherVenues_ListsOnlyThisVenuesActiveOnes()
    {
        Scenario mine = await AVenueWithNights(Friday);
        Scenario theirs = await AVenueWithNights(Friday);

        await using (DrinkItDbContext edit = sql.CreateContext(mine.Venue.Id))
        {
            Product fernet = await edit.Products.SingleAsync(product => product.Id == mine.Fernet.Id);
            fernet.Deactivate();
            await edit.SaveChangesAsync();
        }

        IReadOnlyList<StockedProduct> listed = await Repository(mine.Venue).ListActiveProductsAsync(CancellationToken.None);

        Assert.Equal([mine.Gin.Id], listed.Select(product => product.Id));
        Assert.Equal(20, listed.Single().InitialStock);
        Assert.DoesNotContain(listed, product => product.Id == theirs.Gin.Id);
    }

    [Fact]
    public async Task CarriedOverAsync_WhenSeveralEarlierNightsHaveARow_UsesWhatTheLatestOneLeft()
    {
        Scenario s = await AVenueWithNights(Friday, Saturday, Sunday);
        await s.OpenWithRemaining(s.Nights[0], s.Gin, loaded: 20, remaining: 11);
        await s.OpenWithRemaining(s.Nights[1], s.Gin, loaded: 11, remaining: 4);

        IReadOnlyDictionary<Guid, int> carried = await Repository(s.Venue).CarriedOverAsync(s.Nights[2].Id, CancellationToken.None);

        Assert.Equal(4, carried[s.Gin.Id]);
    }

    [Fact]
    public async Task CarriedOverAsync_WhenAProductHadNoRowBefore_IsNotInTheAnswer()
    {
        Scenario s = await AVenueWithNights(Friday, Saturday);
        await s.OpenWithRemaining(s.Nights[0], s.Gin, loaded: 20, remaining: 11);

        IReadOnlyDictionary<Guid, int> carried = await Repository(s.Venue).CarriedOverAsync(s.Nights[1].Id, CancellationToken.None);

        Assert.False(carried.ContainsKey(s.Fernet.Id));
    }

    // Opening Friday's stock after Saturday's exists must not take Saturday's
    // leftovers as Friday's starting point.
    [Fact]
    public async Task CarriedOverAsync_WhenALaterNightHasARow_IgnoresIt()
    {
        Scenario s = await AVenueWithNights(Friday, Saturday);
        await s.OpenWithRemaining(s.Nights[1], s.Gin, loaded: 5, remaining: 1);

        IReadOnlyDictionary<Guid, int> carried = await Repository(s.Venue).CarriedOverAsync(s.Nights[0].Id, CancellationToken.None);

        Assert.Empty(carried);
    }

    [Fact]
    public async Task AddRangeAsync_WhenRowsAreNew_PersistsThem()
    {
        Scenario s = await AVenueWithNights(Friday);

        await Repository(s.Venue).AddRangeAsync(
            [NightStock.Open(s.Venue.Id, s.Nights[0].Id, s.Gin.Id, 20), NightStock.Open(s.Venue.Id, s.Nights[0].Id, s.Fernet.Id, 8)],
            CancellationToken.None);

        IReadOnlyList<NightStock> listed = await Repository(s.Venue).ListAsync(s.Nights[0].Id, CancellationToken.None);

        Assert.Equal(2, listed.Count);
    }

    // Two requests opening the same night at once: the row that got there
    // first stands and the other is not an error.
    [Fact]
    public async Task AddRangeAsync_WhenARowAlreadyExists_KeepsTheOneThatGotThereFirstAndAddsTheRest()
    {
        Scenario s = await AVenueWithNights(Friday);
        await s.OpenWithRemaining(s.Nights[0], s.Gin, loaded: 20, remaining: 20);

        await Repository(s.Venue).AddRangeAsync(
            [NightStock.Open(s.Venue.Id, s.Nights[0].Id, s.Gin.Id, 99), NightStock.Open(s.Venue.Id, s.Nights[0].Id, s.Fernet.Id, 8)],
            CancellationToken.None);

        IReadOnlyList<NightStock> listed = await Repository(s.Venue).ListAsync(s.Nights[0].Id, CancellationToken.None);

        Assert.Equal(2, listed.Count);
        Assert.Equal(20, listed.Single(row => row.ProductId == s.Gin.Id).Loaded);
        Assert.Equal(8, listed.Single(row => row.ProductId == s.Fernet.Id).Loaded);
    }

    private NightStockRepository Repository(Venue venue) => new(sql.CreateContext(venue.Id));

    private async Task<Scenario> AVenueWithNights(params DateTimeOffset[] startsAt)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        StaffUser kds = StaffUser.Create(venue.Id, $"kds-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, $"till-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Cashier);
        List<Night> nights = [.. startsAt.Select((start, index) =>
            Night.Create(venue.Id, $"Night {index}", start, start.AddHours(7), [kds, till]))];

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        Category category = SeedCategory.For(seed, venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20, category.Id);
        Product fernet = Product.Create(venue.Id, "Fernet", null, null, 4000m, 8, category.Id);
        seed.StaffUsers.AddRange(kds, till);
        seed.Nights.AddRange(nights);
        seed.Products.AddRange(gin, fernet);
        await seed.SaveChangesAsync();

        return new Scenario(sql, venue, nights, gin, fernet);
    }

    private sealed record Scenario(SqlServerFixture Sql, Venue Venue, List<Night> Nights, Product Gin, Product Fernet)
    {
        public async Task OpenWithRemaining(Night night, Product product, int loaded, int remaining)
        {
            await using DrinkItDbContext write = Sql.CreateContext(Venue.Id);
            NightStock row = NightStock.Open(Venue.Id, night.Id, product.Id, loaded);
            write.NightStocks.Add(row);
            await write.SaveChangesAsync();

            await write.NightStocks
                .Where(stock => stock.Id == row.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(stock => stock.Remaining, remaining));
        }
    }
}
