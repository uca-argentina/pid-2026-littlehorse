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

    /// <summary>
    /// US-42: what the phone agreed to pay on top of the balance is not the
    /// difference any more — another phone of the table spent meanwhile, or the
    /// package grew. Refused before anything is charged: the customer sees the
    /// new difference and pays that.
    /// </summary>
    public static readonly Error BalanceChanged =
        new("vip_account.balance_changed", "The table's balance changed: the difference to pay is not what the phone showed.");

    /// <summary>
    /// US-41, criterion 3: another phone of the table spent while this order
    /// was being written. Not an answer for the customer: the order is built
    /// again against the balance as it is now.
    /// </summary>
    public static readonly Error BalanceMoved =
        new("vip_account.balance_moved", "The table's balance changed while the order was being written.");
}
