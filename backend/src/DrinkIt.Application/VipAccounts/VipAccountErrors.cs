using DrinkIt.Application.Common;

namespace DrinkIt.Application.VipAccounts;

public static class VipAccountErrors
{
    /// <summary>
    /// US-41, criterion 4: no account of this venue's night underway opens with
    /// that code. Ran out with its night, regenerated, invented or another
    /// venue's: one answer, so nobody can probe which.
    /// </summary>
    public static readonly Error NotFound =
        new("vip_account.not_found", "That code does not open any table tonight.");

    /// <summary>US-41: the table's balance does not cover the order. US-42 pays the difference.</summary>
    public static readonly Error BalanceShort =
        new("vip_account.balance_short", "The table's balance does not cover this order.");

    /// <summary>
    /// US-41, criterion 3: another phone of the table spent while this order
    /// was being written. Not an answer for the customer: the order is built
    /// again against the balance as it is now.
    /// </summary>
    public static readonly Error BalanceMoved =
        new("vip_account.balance_moved", "The table's balance changed while the order was being written.");
}
