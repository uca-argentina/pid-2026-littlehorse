using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Staff;

public sealed record RenameStaffUserCommand(Guid StaffUserId, string? DisplayName);

/// <summary>
/// US-36: gives an account the name people read instead of its username — a
/// KDS is "Barra Pista" on its board and on the customer's tracking. A blank
/// name takes it away, and the screens fall back to the username.
/// </summary>
public sealed class RenameStaffUserHandler(IStaffUserRepository staffUsers, IKdsBoardNotifier kdsBoards)
{
    public async Task<Result<StaffUserSummary>> HandleAsync(
        RenameStaffUserCommand command,
        CancellationToken cancellationToken)
    {
        StaffUser? user = await staffUsers.GetForUpdateAsync(command.StaffUserId, cancellationToken);

        if (user is null) return StaffUserErrors.NotFound;

        // A name too long is a broken invariant, and Rename throws; the API
        // turns that into a 400, as with every other rule of the account.
        user.Rename(command.DisplayName);

        await staffUsers.SaveChangesAsync(cancellationToken);

        // A KDS shows its name in its board's header: the tablet redraws and
        // asks for it again, without signing in again.
        if (user.Role == StaffRole.Kds) await kdsBoards.NotifyBoardChangedAsync(user.VenueId, user.Id, cancellationToken);

        return StaffUserSummary.Of(user);
    }
}
