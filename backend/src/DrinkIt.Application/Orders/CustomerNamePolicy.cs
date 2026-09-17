using DrinkIt.Application.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>
/// Who the bar calls for an order, and what counts as an answer.
/// </summary>
/// <remarks>
/// A policy in this layer rather than an invariant in the domain, following
/// StaffPasswordPolicy: this is somebody typing into a field and getting it
/// wrong, which is an expected outcome the screen shows — not a bug. The domain
/// keeps the invariant underneath it, that an order always has a name.
///
/// Letters and spaces only, decided on 2026-09-17. It rejects "D'Angelo" and
/// "García-López", which are real surnames; that is the cost of the rule and it
/// was taken knowingly.
/// </remarks>
public static class CustomerNamePolicy
{
    public static readonly Error Required =
        new("order.name_required", "We need a name to call you by at the bar.");

    public static readonly Error NeedsASurname =
        new("order.name_needs_surname", "Give us your first name and your surname.");

    public static readonly Error OnlyLetters =
        new("order.name_only_letters", "The name can only have letters.");

    public static readonly Error TooLong =
        new("order.name_too_long", $"The name cannot be longer than {Order.CustomerNameMaxLength} characters.");

    /// <summary>
    /// The name as the bar should see it, or why it cannot be used. Spare
    /// spaces are tidied rather than rejected: somebody typing on a phone at
    /// 3 AM leaves them everywhere, and it is not their problem.
    /// </summary>
    public static Result<string> Read(string? name)
    {
        string[] words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);

        if (words.Length == 0) return Required;

        // Checked before the surname: "Euge 21" is a name with a problem, and
        // being told to add a surname would send somebody down the wrong path.
        if (!words.All(word => word.All(char.IsLetter))) return OnlyLetters;
        if (words.Length < 2) return NeedsASurname;

        string tidy = string.Join(' ', words);

        return tidy.Length > Order.CustomerNameMaxLength ? TooLong : tidy;
    }
}
