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
}
