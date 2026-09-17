using System.Security.Cryptography;
using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Orders;

/// <summary>
/// The secret half of the link somebody follows to watch their order.
/// </summary>
/// <remarks>
/// A customer of this app has no account — the brief asks that ordering need
/// none — so there is nothing to prove an order is theirs except something only
/// they hold. That is this: 128 random bits, handed out once when the order is
/// placed and carried in the link.
///
/// It is deliberately not the order's code. The code is the order's name: said
/// out loud at the bar, printed on a ticket, safe in a log. This never appears
/// in any of those places. When customer accounts arrive, the rule becomes "you
/// own this order, or you are holding its token", and neither half has to
/// change.
/// </remarks>
public sealed record TrackingToken
{
    public static class ErrorCodes
    {
        public const string Invalid = "tracking_token.invalid";
    }

    /// <summary>
    /// 32 hex digits: the 128 bits underneath are what make guessing hopeless,
    /// and there are 340 undecillion of them for every order code.
    /// </summary>
    public const int Length = 32;

    private TrackingToken(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// From the system's cryptographic source and not from Random or a Guid: a
    /// value whose only job is to be unguessable cannot come from a generator
    /// whose next output can be worked out from the last one.
    /// </summary>
    public static TrackingToken New() =>
        new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(Length / 2)));

    public static TrackingToken Parse(string? value)
    {
        if (!IsWellShaped(value)) throw new DomainException(ErrorCodes.Invalid, "That is not a tracking link.");

        return new TrackingToken(value!);
    }

    /// <summary>
    /// Whether that is this token, compared in constant time.
    /// </summary>
    /// <remarks>
    /// An ordinary string comparison stops at the first character that differs,
    /// and how long it took says how much of the guess was right. This is the
    /// one value in the system worth guessing, so it is the one comparison
    /// worth making blind to its own answer.
    /// </remarks>
    public bool Matches(string? candidate) =>
        IsWellShaped(candidate)
        && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(Value),
            System.Text.Encoding.ASCII.GetBytes(candidate!));

    public override string ToString() => Value;

    private static bool IsWellShaped(string? value) =>
        value is { Length: Length } && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
}
