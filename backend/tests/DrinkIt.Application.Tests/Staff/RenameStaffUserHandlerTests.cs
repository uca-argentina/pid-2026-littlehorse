using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Staff;

/// <summary>
/// US-36: the name people read instead of the username — "Barra Pista" on the
/// board's header and on the customer's "Retiralo en…" line.
/// </summary>
public class RenameStaffUserHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static StaffUser ABar() => StaffUser.Create(TheVenue, "barra.pista", "hash", StaffRole.Kds);

    [Fact]
    public async Task HandleAsync_WhenTheAccountIsThisVenues_GivesItTheNameAndSavesIt()
    {
        StaffUser bar = ABar();
        StaffUsersInMemory staff = new(bar);

        Result<StaffUserSummary> result = await new RenameStaffUserHandler(staff, new SpyBoards())
            .HandleAsync(new RenameStaffUserCommand(bar.Id, "Barra Pista"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Barra Pista", bar.DisplayName);
        Assert.Equal("Barra Pista", result.Value.DisplayName);
        Assert.Equal(1, staff.Saves);
    }

    // Another venue's account looks exactly like this too: the filter hides it.
    [Fact]
    public async Task HandleAsync_WhenNobodyHereHasThatId_FailsWithoutSaving()
    {
        StaffUsersInMemory staff = new(ABar());

        Result<StaffUserSummary> result = await new RenameStaffUserHandler(staff, new SpyBoards())
            .HandleAsync(new RenameStaffUserCommand(Guid.CreateVersion7(), "Barra Pista"), CancellationToken.None);

        Assert.Equal(StaffUserErrors.NotFound, result.Error);
        Assert.Equal(0, staff.Saves);
    }

    // A tablet already signed in shows the new name without signing in again:
    // its board redraws, and asks for its name with the queue.
    [Fact]
    public async Task HandleAsync_WhenTheAccountIsAKds_TellsItsBoard()
    {
        StaffUser bar = ABar();
        SpyBoards boards = new();

        await new RenameStaffUserHandler(new StaffUsersInMemory(bar), boards)
            .HandleAsync(new RenameStaffUserCommand(bar.Id, "Barra Pista"), CancellationToken.None);

        Assert.Equal((TheVenue, bar.Id), Assert.Single(boards.Notified));
    }

    [Fact]
    public async Task HandleAsync_WhenTheAccountHasNoBoard_TellsNoBoard()
    {
        StaffUser waiter = StaffUser.Create(TheVenue, "martin.p", "hash", StaffRole.Waiter);
        SpyBoards boards = new();

        await new RenameStaffUserHandler(new StaffUsersInMemory(waiter), boards)
            .HandleAsync(new RenameStaffUserCommand(waiter.Id, "Martín"), CancellationToken.None);

        Assert.Empty(boards.Notified);
    }

    private sealed class SpyBoards : IKdsBoardNotifier
    {
        public List<(Guid VenueId, Guid KdsId)> Notified { get; } = [];

        public Task NotifyBoardChangedAsync(Guid venueId, Guid kdsId, CancellationToken cancellationToken)
        {
            Notified.Add((venueId, kdsId));
            return Task.CompletedTask;
        }
    }
}
