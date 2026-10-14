namespace DrinkIt.Application.Common;

/// <summary>
/// Who is signed in on the current request, for the audit columns (US-30).
/// Read from the token and never from anything the caller sends.
/// </summary>
public interface ICurrentStaffUser
{
    /// <summary>
    /// The username, or null when the request carries no signed-in user: a
    /// customer ordering, or the seeder at startup. Null is the honest answer
    /// there — a made-up user would fill the column with a lie.
    /// </summary>
    string? Username { get; }

    /// <summary>
    /// The account's id, or null on the same requests as <see cref="Username"/>.
    /// What tells one KDS from another (US-36): a tablet only ever sees and
    /// moves the orders of the bar it is signed in as.
    /// </summary>
    Guid? Id { get; }
}
