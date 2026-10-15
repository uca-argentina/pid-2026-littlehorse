using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Tests.VipAccounts;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Application.Tests.Nights;

public class RegenerateTableCodeHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));

    private static readonly Night Saturday = Night.Create(
        TheVenue,
        "Saturday",
        Opening,
        Opening.AddHours(7),
        [
            StaffUser.Create(TheVenue, "main-bar", "hash", StaffRole.Kds),
            StaffUser.Create(TheVenue, "till-1", "hash", StaffRole.Cashier),
            StaffUser.Create(TheVenue, "martin", "hash", StaffRole.Waiter),
        ]);

    private static readonly Table TableFive = Table.Create(TheVenue, 5, 8, null, Opening.AddDays(-1));

    [Fact]
    public async Task HandleAsync_WhenTheTableIsInTheNight_GivesItANewCodeAndSaves()
    {
        VipAccount five = VipAccount.Open(Saturday, TableFive, 150_000m, Opening.AddHours(-3));
        string leaked = five.AccessCode;
        NightsInMemory nights = new(Saturday);

        Result<NightTableCode> result = await HandlerOver(nights, new VipAccountsInMemory(five))
            .HandleAsync(Saturday.Id, TableFive.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(leaked, result.Value.AccessCode);
        Assert.Equal(five.AccessCode, result.Value.AccessCode);
        Assert.Equal(1, nights.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNightDoesNotSellThatTable_FailsTableNotInNight()
    {
        NightsInMemory nights = new(Saturday);

        Result<NightTableCode> result = await HandlerOver(nights, new VipAccountsInMemory())
            .HandleAsync(Saturday.Id, TableFive.Id, CancellationToken.None);

        Assert.Equal(NightErrors.TableNotInNight, result.Error);
        Assert.Equal(0, nights.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenTheVenueHasNoSuchNight_FailsNotFound()
    {
        Result<NightTableCode> result = await HandlerOver(new NightsInMemory(), new VipAccountsInMemory())
            .HandleAsync(Guid.CreateVersion7(), TableFive.Id, CancellationToken.None);

        Assert.Equal(NightErrors.NotFound, result.Error);
    }

    private static RegenerateTableCodeHandler HandlerOver(NightsInMemory nights, VipAccountsInMemory accounts) =>
        new(nights, accounts, new FixedClock(Opening.AddHours(1)));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
