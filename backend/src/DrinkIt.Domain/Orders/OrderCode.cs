using System.Globalization;
using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Orders;

/// <summary>
/// The short code a customer claims their drinks with: one letter, a hyphen and
/// four digits, "K-4821".
/// </summary>
/// <remarks>
/// Shaped for a room with loud music, which is the only room this app runs in.
/// The hyphen is what tells the letter from the digits when somebody reads the
/// code out loud — with it, the O of "O-0000" can only be the letter, so the
/// whole alphabet is usable.
///
/// It runs in order rather than at random: A-0000, A-0001, and the letter moves
/// on when the four digits are full. 260.000 codes later it starts over, by
/// which point nobody is still waiting on the first A-0000. Running in order is
/// also what makes two simultaneous orders impossible to collide — the database
/// hands out the next one — which is criterion 4 of US-11.
/// </remarks>
public sealed record OrderCode
{
    public static class ErrorCodes
    {
        public const string Invalid = "order_code.invalid";
    }

    private const int BlockSize = 10_000;

    private const char FirstLetter = 'A';

    private const char LastLetter = 'Z';

    private OrderCode(char letter, int number)
    {
        Letter = letter;
        Number = number;
    }

    /// <summary>What a venue's very first order is called.</summary>
    public static OrderCode First { get; } = new(FirstLetter, 0);

    public char Letter { get; }

    public int Number { get; }

    /// <summary>What is printed, said and searched for: "K-4821".</summary>
    public string Value => $"{Letter}-{Number.ToString("D4", CultureInfo.InvariantCulture)}";

    public static OrderCode Parse(string value)
    {
        if (!IsWellShaped(value)) throw new DomainException(ErrorCodes.Invalid, "That is not an order code.");

        return new OrderCode(value[0], int.Parse(value[2..], CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Reads a code that somebody else typed, without throwing when it is not
    /// one.
    /// </summary>
    /// <remarks>
    /// A code arriving from a URL is not a broken invariant: it is a stranger
    /// working through codes, or a link cut in half by a chat app. Both deserve
    /// the same "no such order" as a code nobody has, so whatever asks can say
    /// that instead of having to catch an exception to do it.
    /// </remarks>
    public static bool TryParse(string? value, out OrderCode? code)
    {
        code = value is not null && IsWellShaped(value)
            ? new OrderCode(value[0], int.Parse(value[2..], CultureInfo.InvariantCulture))
            : null;

        return code is not null;
    }

    /// <summary>
    /// The code after this one. The four digits are a block and never grow a
    /// fifth: full, they roll over and the letter moves on.
    /// </summary>
    public OrderCode Next()
    {
        if (Number < BlockSize - 1) return new OrderCode(Letter, Number + 1);

        return Letter == LastLetter ? First : new OrderCode((char)(Letter + 1), 0);
    }

    public override string ToString() => Value;

    private static bool IsWellShaped(string value) =>
        value.Length == 6
        && value[0] is >= FirstLetter and <= LastLetter
        && value[1] == '-'
        && value[2..].All(char.IsAsciiDigit);
}
