using DrinkIt.Application.Common;
using DrinkIt.Application.Tables;
using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Application.Orders;

/// <summary>
/// The table's balance (§8.1, US-41): the order is paid from the account the
/// QR opened, and carried to that table instead of picked up at the bar. When
/// the balance falls short, the phone pays the difference (US-42).
/// </summary>
/// <remarks>
/// Only tracked here. The order's repository writes the order and the spent
/// balance in one transaction, and the account's concurrency token is what
/// settles two phones of the same table ordering at once: the loser comes
/// back as <see cref="VipAccountErrors.BalanceMoved"/> and is built again.
/// </remarks>
public sealed class VipBalancePaymentStrategy(
    IVipAccountRepository vipAccounts,
    ITableRepository tables,
    TimeProvider clock) : IPaymentStrategy
{
    public PaymentMethod Method => PaymentMethod.VipBalance;

    public async Task<Error?> SettleAsync(Order order, PaymentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TableCode)) return VipAccountErrors.NotFound;

        VipAccount? account = await vipAccounts.FindByAccessCodeAsync(request.TableCode, cancellationToken);

        // The order belongs to the night underway, so an account of any other
        // night is one whose night is not on: it ran out (US-40, criterion 3).
        if (account is null || account.NightId != order.NightId) return VipAccountErrors.NotFound;

        // US-42: the balance gives all it has, up to the total, and the phone
        // pays the rest. What it agreed to pay has to be exactly that rest:
        // checked before anything is spent, so a difference that moved is
        // refused with the balance untouched (criterion 3).
        decimal fromBalance = Math.Min(account.Balance, order.Total);

        if ((request.DigitalAmount ?? 0) != order.Total - fromBalance) return VipAccountErrors.BalanceChanged;

        IReadOnlyList<Table> table = await tables.ListByIdsAsync([account.TableId], cancellationToken);

        // Tables are never deleted, so this is a row nobody expected to miss.
        // It still answers as a code that opens nothing, not as a crash.
        if (table.Count == 0) return VipAccountErrors.NotFound;

        // The difference is paid with the simulated digital payment, which has
        // nothing to call: it settles here, in the same write as the balance.
        if (fromBalance > 0) account.Spend(fromBalance);

        order.ServeAtTable(account.TableId, table[0].Number);
        order.PayFromBalance(clock.GetUtcNow(), fromBalance);

        return null;
    }
}
