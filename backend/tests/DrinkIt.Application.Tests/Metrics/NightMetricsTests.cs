using DrinkIt.Application.Metrics;
using DrinkIt.Application.Nights;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Metrics;

/// <summary>US-47, US-48 and US-49: what a night's report says, worked out from its paid orders and its bars.</summary>
public class NightMetricsTests
{
    private static readonly DateTimeOffset TenPast = new(2026, 10, 11, 2, 10, 0, TimeSpan.Zero);

    private static readonly Guid Pista = Guid.CreateVersion7();
    private static readonly Guid Vip = Guid.CreateVersion7();

    private static readonly Guid Gin = Guid.CreateVersion7();
    private static readonly Guid Fernet = Guid.CreateVersion7();

    private static readonly NightStockSheet NoBars = new(Guid.CreateVersion7(), []);

    private static PaidOrder Paid(
        PaymentMethod method = PaymentMethod.Digital,
        decimal total = 1000m,
        Guid? bar = null,
        OrderStatus status = OrderStatus.Delivered,
        int? readyAfterMinutes = 5,
        int? deliveredAfterMinutes = 2) =>
        new(
            bar ?? Pista,
            method,
            total,
            status,
            TenPast,
            readyAfterMinutes is int ready ? TenPast.AddMinutes(ready) : null,
            readyAfterMinutes is int r && deliveredAfterMinutes is int delivered ? TenPast.AddMinutes(r + delivered) : null);

    private static NightMetrics Build(
        IEnumerable<PaidOrder> orders,
        IEnumerable<ProductSales>? products = null,
        NightStockSheet? sheet = null,
        IEnumerable<NightBar>? bars = null) =>
        NightMetrics.Build(new NightSales([.. bars ?? []], [.. orders], [.. products ?? []]), sheet ?? NoBars);

    // US-48, criterion 1.
    [Fact]
    public void Build_Always_SumsWhatWasPaidAndHowManyOrders()
    {
        NightMetrics metrics = Build([Paid(total: 1000m), Paid(total: 2500m), Paid(total: 500m)]);

        Assert.Equal(4000m, metrics.Sold);
        Assert.Equal(3, metrics.Orders);
    }

    [Fact]
    public void Build_Always_SplitsTheSalesByPaymentMethod()
    {
        NightMetrics metrics = Build(
        [
            Paid(PaymentMethod.Cash, 1000m),
            Paid(PaymentMethod.Cash, 2000m),
            Paid(PaymentMethod.Digital, 500m),
        ]);

        Assert.Equal(new SalesByMethod(PaymentMethod.Cash, 2, 3000m), metrics.ByMethod.Single(row => row.Method == PaymentMethod.Cash));
        Assert.Equal(new SalesByMethod(PaymentMethod.Digital, 1, 500m), metrics.ByMethod.Single(row => row.Method == PaymentMethod.Digital));
    }

    // US-48, criterion 2 (US-42): a table's order that paid part from its
    // balance and the rest from the phone adds each part to its own row, and
    // counts as an order in both.
    [Fact]
    public void Build_WhenATableOrderPaidADifference_AddsEachPartToItsMethod()
    {
        NightMetrics metrics = Build(
        [
            Paid(PaymentMethod.Digital, 500m),
            Paid(PaymentMethod.VipBalance, 9000m) with { BalanceAmount = 5000m },
            Paid(PaymentMethod.VipBalance, 2000m) with { BalanceAmount = 2000m },
        ]);

        Assert.Equal(11500m, metrics.Sold);
        Assert.Equal(new SalesByMethod(PaymentMethod.Digital, 2, 4500m), metrics.ByMethod.Single(row => row.Method == PaymentMethod.Digital));
        Assert.Equal(new SalesByMethod(PaymentMethod.VipBalance, 2, 7000m), metrics.ByMethod.Single(row => row.Method == PaymentMethod.VipBalance));
    }

    // A balance at zero paid nothing: the whole order is digital, not a VIP sale of 0.
    [Fact]
    public void Build_WhenATableOrderGotNothingFromTheBalance_CountsOnlyAsDigital()
    {
        NightMetrics metrics = Build([Paid(PaymentMethod.VipBalance, 9000m) with { BalanceAmount = 0m }]);

        Assert.Equal(new SalesByMethod(PaymentMethod.Digital, 1, 9000m), metrics.ByMethod.Single(row => row.Method == PaymentMethod.Digital));
        Assert.Equal(new SalesByMethod(PaymentMethod.VipBalance, 0, 0m), metrics.ByMethod.Single(row => row.Method == PaymentMethod.VipBalance));
    }

    // The screen draws the same three rows every night, so a method that sold
    // nothing is a zero and not a missing row.
    [Fact]
    public void Build_WhenAMethodSoldNothing_StillListsItAtZero()
    {
        NightMetrics metrics = Build([Paid(PaymentMethod.Cash)]);

        Assert.Equal(
            [PaymentMethod.Cash, PaymentMethod.Digital, PaymentMethod.VipBalance],
            metrics.ByMethod.Select(row => row.Method));
        Assert.Equal(new SalesByMethod(PaymentMethod.VipBalance, 0, 0m), metrics.ByMethod.Single(row => row.Method == PaymentMethod.VipBalance));
    }

    [Fact]
    public void Build_WithNoOrders_IsAllZeros()
    {
        NightMetrics metrics = Build([]);

        Assert.Equal(0m, metrics.Sold);
        Assert.Equal(0, metrics.Orders);
        Assert.Equal(0m, metrics.AverageTicket);
        Assert.Null(metrics.PaidToReady.Average);
    }

    [Fact]
    public void Build_Always_DividesWhatWasSoldByTheOrders()
    {
        NightMetrics metrics = Build([Paid(total: 1000m), Paid(total: 2000m)]);

        Assert.Equal(1500m, metrics.AverageTicket);
    }

    // US-47, criterion 1. Ten orders ready after 1 to 10 minutes: 5.5 on
    // average, and the slowest tenth is the one that took 10.
    [Fact]
    public void Build_Always_AveragesFromPaidToReadyAndTakesTheWorstTenth()
    {
        NightMetrics metrics = Build(Enumerable.Range(1, 10).Select(minutes => Paid(readyAfterMinutes: minutes)));

        Assert.Equal(TimeSpan.FromMinutes(5.5), metrics.PaidToReady.Average);
        Assert.Equal(TimeSpan.FromMinutes(10), metrics.PaidToReady.WorstTenth);
    }

    // The narrative's own case: the average hides the few that took long. The
    // 90th percentile would hide them too, landing on a quick one; the
    // average of the slowest tenth is what shows them.
    [Fact]
    public void Build_WhenAFewOrdersTookLong_ShowsThemInTheWorstTenth()
    {
        IEnumerable<PaidOrder> quick = Enumerable.Range(0, 18).Select(_ => Paid(readyAfterMinutes: 5));
        IEnumerable<PaidOrder> slow = Enumerable.Range(0, 2).Select(_ => Paid(readyAfterMinutes: 30));

        NightMetrics metrics = Build(quick.Concat(slow));

        Assert.Equal(TimeSpan.FromMinutes(7.5), metrics.PaidToReady.Average);
        Assert.Equal(TimeSpan.FromMinutes(30), metrics.PaidToReady.WorstTenth);
    }

    // Still in the queue: nothing to measure yet, and not "unmeasured" either.
    [Fact]
    public void Build_WhenAnOrderIsNotReadyYet_LeavesItOutOfTheTimes()
    {
        NightMetrics metrics = Build(
        [
            Paid(readyAfterMinutes: 4),
            Paid(status: OrderStatus.Queued, readyAfterMinutes: null),
        ]);

        Assert.Equal(TimeSpan.FromMinutes(4), metrics.PaidToReady.Average);
        Assert.Equal(0, metrics.Unmeasured);
    }

    [Fact]
    public void Build_Always_MeasuresFromReadyToDeliveredOnlyWhatWasDelivered()
    {
        NightMetrics metrics = Build(
        [
            Paid(readyAfterMinutes: 5, deliveredAfterMinutes: 2),
            Paid(readyAfterMinutes: 5, deliveredAfterMinutes: 4),
            Paid(status: OrderStatus.Ready, readyAfterMinutes: 5, deliveredAfterMinutes: null),
        ]);

        Assert.Equal(TimeSpan.FromMinutes(3), metrics.ReadyToDelivered.Average);
    }

    // US-47, criterion 3: orders from before the stamps were kept are not
    // given made-up times. They are counted, so the screen can say so.
    [Fact]
    public void Build_WhenAnOrderGotPastReadyWithoutItsStamp_CountsItAsUnmeasured()
    {
        NightMetrics metrics = Build(
        [
            Paid(readyAfterMinutes: 6),
            Paid(status: OrderStatus.Delivered, readyAfterMinutes: null),
            Paid(status: OrderStatus.Ready, readyAfterMinutes: null),
        ]);

        Assert.Equal(2, metrics.Unmeasured);
        Assert.Equal(TimeSpan.FromMinutes(6), metrics.PaidToReady.Average);
    }

    // US-47, criterion 2.
    [Fact]
    public void Build_Always_SplitsPaidToReadyByBar()
    {
        NightBar[] bars = [new(Pista, "Barra Pista"), new(Vip, "Barra VIP")];

        NightMetrics metrics = Build(
        [
            Paid(bar: Pista, readyAfterMinutes: 4),
            Paid(bar: Pista, readyAfterMinutes: 6),
            Paid(bar: Vip, readyAfterMinutes: 10),
        ], bars: bars);

        Assert.Equal(
            [new BarTimes(Pista, "Barra Pista", new StageTimes(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(6))),
             new BarTimes(Vip, "Barra VIP", new StageTimes(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)))],
            metrics.ByBar);
    }

    // Every bar of the night has a row, even one that has made nothing yet.
    [Fact]
    public void Build_WhenABarHasMadeNothing_ListsItWithoutTimes()
    {
        NightBar[] bars = [new(Pista, "Barra Pista"), new(Vip, "Barra VIP")];

        NightMetrics metrics = Build([Paid(bar: Pista)], bars: bars);

        Assert.Equal(new StageTimes(null, null), metrics.ByBar.Single(bar => bar.KdsId == Vip).PaidToReady);
    }

    // US-49, criterion 1: by units, the ten first, with what each billed.
    [Fact]
    public void Build_Always_RanksTheTenMostOrderedByUnits()
    {
        ProductSales[] products =
        [
            .. Enumerable.Range(1, 12).Select(units => new ProductSales(Guid.CreateVersion7(), $"Product {units}", units, units * 100m)),
        ];

        NightMetrics metrics = Build([], products);

        Assert.Equal(10, metrics.Top.Count);
        Assert.Equal([12, 11, 10, 9, 8, 7, 6, 5, 4, 3], metrics.Top.Select(product => product.Units));
        Assert.Equal(1200m, metrics.Top[0].Billed);
    }

    [Fact]
    public void Build_Always_CountsTheUnitsOfEveryProduct()
    {
        ProductSales[] products =
        [
            .. Enumerable.Range(1, 12).Select(units => new ProductSales(Guid.CreateVersion7(), $"Product {units}", units, 0m)),
        ];

        Assert.Equal(78, Build([], products).Units);
    }

    // US-49, criterion 2: what was loaded of it across the night's bars, and
    // whether every bar ran out of it.
    [Fact]
    public void Build_Always_CarriesWhatWasLoadedAndWhetherItRanOut()
    {
        NightStockSheet sheet = new(Guid.CreateVersion7(),
        [
            new NightStockBar(Pista, "Barra Pista", [new NightStockLine(Gin, "Gin Tonic", 48, 48, 0), new NightStockLine(Fernet, "Fernet", 80, 74, 0)]),
            new NightStockBar(Vip, "Barra VIP", [new NightStockLine(Gin, "Gin Tonic", 12, 12, 0), new NightStockLine(Fernet, "Fernet", 20, 8, 0)]),
        ]);
        ProductSales[] products = [new(Gin, "Gin Tonic", 60, 270000m), new(Fernet, "Fernet", 82, 328000m)];

        NightMetrics metrics = Build([], products, sheet);

        Assert.Equal(new TopProduct(Fernet, "Fernet", 82, 328000m, 100, false), metrics.Top[0]);
        Assert.Equal(new TopProduct(Gin, "Gin Tonic", 60, 270000m, 60, true), metrics.Top[1]);
        Assert.Equal(1, metrics.SoldOutProducts);
    }

    // Never loaded into any bar tonight is not "ran out": there was nothing.
    [Fact]
    public void Build_WhenAProductWasNeverLoaded_DoesNotCallItSoldOut()
    {
        NightStockSheet sheet = new(Guid.CreateVersion7(), [new NightStockBar(Pista, "Barra Pista", [new NightStockLine(Gin, "Gin Tonic", 0, 0, 0)])]);

        NightMetrics metrics = Build([], [new ProductSales(Gin, "Gin Tonic", 1, 100m)], sheet);

        Assert.False(metrics.Top[0].SoldOut);
        Assert.Equal(0, metrics.SoldOutProducts);
    }
}
