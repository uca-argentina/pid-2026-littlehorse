using DrinkIt.Api.Tenancy;
using DrinkIt.Application.Payments;
using DrinkIt.Application.Venues;

namespace DrinkIt.Api.Features.Payments;

/// <summary>
/// US-24: once a minute, whatever nobody paid within the payment window is
/// canceled and its drinks go back on the menu.
/// </summary>
/// <remarks>
/// Venue by venue, each in a scope of its own that resolves that venue first —
/// exactly what a request does — so the global query filter keeps every other
/// venue's orders out and nothing here reads across tenants.
/// </remarks>
public sealed partial class ExpireUnpaidOrdersService(IServiceProvider services, ILogger<ExpireUnpaidOrdersService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Every);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(services, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The next minute tries again; one bad pass must not stop them all.
                LogPassFailed(logger, exception);
            }
        }
    }

    /// <summary>One pass over every venue.</summary>
    public static async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> venues;

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            venues = await scope.ServiceProvider.GetRequiredService<IVenueDirectory>().AllIdsAsync(cancellationToken);
        }

        foreach (Guid venueId in venues)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();

            scope.ServiceProvider.GetRequiredService<CurrentVenue>().Resolve(venueId);

            await scope.ServiceProvider.GetRequiredService<ExpireUnpaidOrdersHandler>().HandleAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass canceling unpaid orders failed.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
