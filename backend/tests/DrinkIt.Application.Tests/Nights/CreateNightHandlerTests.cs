using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Tests.Staff;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Nights;

public class CreateNightHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);

    private static readonly StaffUser MainBar = StaffUser.Create(TheVenue, "main-bar", "hash", StaffRole.Kds);
    private static readonly StaffUser Till = StaffUser.Create(TheVenue, "till-1", "hash", StaffRole.Cashier);
    private static readonly StaffUser Martin = StaffUser.Create(TheVenue, "martin", "hash", StaffRole.Waiter);

    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_AddsTheNightToTheVenueOfTheSignedInAdministrator()
    {
        NightsInMemory nights = new();

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            ACommand(MainBar.Id, Till.Id, Martin.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TheVenue, nights.Added!.VenueId);
        Assert.Equal("Saturday 10/10", nights.Added.Name);
        Assert.Equal([MainBar.Id, Till.Id, Martin.Id], nights.Added.CrewIds);
    }

    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_ReturnsWhatTheListingShows()
    {
        NightsInMemory nights = new();

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            ACommand(MainBar.Id, Till.Id),
            CancellationToken.None);

        Assert.Equal(nights.Added!.Id, result.Value.Id);
        Assert.Equal("Saturday 10/10", result.Value.Name);
        Assert.Equal(Opening, result.Value.StartsAt);
        Assert.Equal(Closing, result.Value.EndsAt);
        Assert.Equal([MainBar.Id, Till.Id], result.Value.CrewIds);
    }

    // US-37: a new night is saved with the storeroom split among its KDS,
    // both or neither. Only the KDS of its crew, and the server's clock
    // decides what the storeroom has; how much each gets is the night's rule.
    [Fact]
    public async Task HandleAsync_WhenTheNightIsCreated_AddsItWithItsKdsAndTheServerClock()
    {
        NightsInMemory nights = new();

        await HandlerOver(nights).HandleAsync(ACommand(MainBar.Id, Till.Id, Martin.Id), CancellationToken.None);

        Assert.Equal([MainBar.Id], nights.AddedBars!.Select(bar => bar.Id));
        Assert.Equal(Now, nights.AddedAt);
    }

    // Criterion 1: two nights of the same venue cannot share a single minute,
    // or an order confirmed in it would not know which one it belongs to.
    [Fact]
    public async Task HandleAsync_WhenItOverlapsAnotherNightOfTheVenue_FailsWithoutCreatingAnything()
    {
        Night friday = Night.Create(TheVenue, "Friday 9/10", Opening.AddHours(-20), Opening.AddHours(1), [MainBar, Till]);
        NightsInMemory nights = new(friday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            ACommand(MainBar.Id, Till.Id),
            CancellationToken.None);

        Assert.Equal(NightErrors.Overlaps, result.Error);
        Assert.Null(nights.Added);
    }

    // An id that is not in this venue — never existed, or belongs to another
    // one — reads the same: the global query filter hides both alike.
    [Fact]
    public async Task HandleAsync_WhenACrewMemberIsNotInTheVenue_FailsWithoutCreatingAnything()
    {
        NightsInMemory nights = new();

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            ACommand(MainBar.Id, Till.Id, Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.Equal(NightErrors.CrewMemberNotFound, result.Error);
        Assert.Null(nights.Added);
    }

    private static CreateNightCommand ACommand(params Guid[] crewIds) =>
        new("Saturday 10/10", Opening, Closing, crewIds);

    private static readonly DateTimeOffset Now = Opening.AddHours(-5);

    private static CreateNightHandler HandlerOver(NightsInMemory nights) =>
        new(nights, new StaffUsersInMemory(MainBar, Till, Martin), new TheCurrentVenue(), new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }

    private sealed class TheCurrentVenue : ICurrentVenue
    {
        public Guid Id => TheVenue;
    }
}
