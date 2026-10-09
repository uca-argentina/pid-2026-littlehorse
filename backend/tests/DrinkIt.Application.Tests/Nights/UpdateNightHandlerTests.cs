using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Tests.Staff;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Nights;

public class UpdateNightHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);

    private static readonly StaffUser MainBar = StaffUser.Create(TheVenue, "main-bar", "hash", StaffRole.Kds);
    private static readonly StaffUser Till = StaffUser.Create(TheVenue, "till-1", "hash", StaffRole.Cashier);
    private static readonly StaffUser Martin = StaffUser.Create(TheVenue, "martin", "hash", StaffRole.Waiter);

    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_SavesTheChangesAndReturnsThem()
    {
        Night saturday = ASaturday();
        NightsInMemory nights = new(saturday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday, late", Opening, Closing.AddHours(1), [MainBar.Id, Till.Id, Martin.Id]),
            CancellationToken.None);

        Assert.Equal("Saturday, late", result.Value.Name);
        Assert.Equal(Closing.AddHours(1), saturday.EndsAt);
        Assert.Equal(1, nights.Saves);
    }

    // A night never overlaps itself: moving its own end an hour is not a clash.
    [Fact]
    public async Task HandleAsync_WhenOnlyItsOwnHoursAreInTheWay_Succeeds()
    {
        Night saturday = ASaturday();
        NightsInMemory nights = new(saturday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening.AddHours(1), Closing.AddHours(1), [MainBar.Id, Till.Id]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNewHoursOverlapAnotherNight_FailsWithoutSaving()
    {
        Night saturday = ASaturday();
        Night sunday = Night.Create(TheVenue, "Sunday", Closing.AddHours(12), Closing.AddHours(19), [MainBar, Till]);
        NightsInMemory nights = new(saturday, sunday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening, Closing.AddHours(13), [MainBar.Id, Till.Id]),
            CancellationToken.None);

        Assert.Equal(NightErrors.Overlaps, result.Error);
        Assert.Equal(0, nights.Saves);
    }

    // Unknown, or another venue's: the global query filter hides both alike.
    [Fact]
    public async Task HandleAsync_WhenTheVenueHasNoSuchNight_FailsWithNotFound()
    {
        Result<NightSummary> result = await HandlerOver(new NightsInMemory()).HandleAsync(
            new UpdateNightCommand(Guid.CreateVersion7(), "Saturday", Opening, Closing, [MainBar.Id, Till.Id]),
            CancellationToken.None);

        Assert.Equal(NightErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenACrewMemberIsNotInTheVenue_FailsWithoutSaving()
    {
        Night saturday = ASaturday();
        NightsInMemory nights = new(saturday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening, Closing, [MainBar.Id, Till.Id, Guid.CreateVersion7()]),
            CancellationToken.None);

        Assert.Equal(NightErrors.CrewMemberNotFound, result.Error);
        Assert.Equal(0, nights.Saves);
    }

    // The clock is the handler's, not the request's: an administrator cannot
    // edit a finished night by sending a moment of their own.
    [Fact]
    public async Task HandleAsync_WhenTheNightIsUnderway_JudgesTheEditAgainstTheServerClock()
    {
        Night saturday = ASaturday();
        NightsInMemory nights = new(saturday);

        Result<NightSummary> result = await HandlerOver(nights, now: Opening.AddHours(3)).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening, Opening.AddHours(1), [MainBar.Id, Till.Id]),
            CancellationToken.None);

        Assert.Equal(Opening.AddHours(3), result.Value.EndsAt);
    }

    private static Night ASaturday() => Night.Create(TheVenue, "Saturday", Opening, Closing, [MainBar, Till]);

    private static UpdateNightHandler HandlerOver(NightsInMemory nights, DateTimeOffset? now = null) =>
        new(nights, new StaffUsersInMemory(MainBar, Till, Martin), new FixedClock(now ?? Opening.AddHours(-2)));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
