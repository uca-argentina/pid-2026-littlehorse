using DrinkIt.Application.Common;

using DrinkIt.Domain.Nights;


namespace DrinkIt.Application.Nights;


public sealed class MarkNightInactiveHandler(INightRepository nights)
{
    public async Task<Result<NightSummary>> HandleAsync(UpdateNightCommand command, CancellationToken cancellationToken)
    {
        Night? night = await nights.GetForUpdateAsync(command.Id, cancellationToken);
        if (night is null) return NightErrors.NotFound;

        night.MarkInactive();

        return NightSummary.Of(night);
    }
}
