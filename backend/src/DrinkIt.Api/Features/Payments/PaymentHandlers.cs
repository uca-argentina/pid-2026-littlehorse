using DrinkIt.Application.Payments;

namespace DrinkIt.Api.Features.Payments;

/// <summary>What the payment endpoints and the expiry need. Scoped, like the DbContext below them.</summary>
internal static class PaymentHandlers
{
    public static IServiceCollection AddPaymentHandlers(this IServiceCollection services) => services
        .AddScoped<ApplyPaymentHandler>()
        .AddScoped<ReturnFromPaymentHandler>()
        .AddScoped<ExpireUnpaidOrdersHandler>()
        .AddHostedService<ExpireUnpaidOrdersService>();
}
