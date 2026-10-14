using DrinkIt.Application.Common;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tables;

public sealed record CreateTableCommand(int Number);

/// <summary>
/// Adds a table to the venue the signed-in administrator belongs to. The venue
/// comes from <see cref="ICurrentVenue"/>, which reads the token claim, so an
/// administrator cannot add one anywhere else by changing the request.
/// </summary>
public sealed class CreateTableHandler(ITableRepository tables, ICurrentVenue currentVenue, TimeProvider clock)
{
    public async Task<Result<TableSummary>> HandleAsync(CreateTableCommand command, CancellationToken cancellationToken)
    {
        if (await tables.NumberExistsAsync(command.Number, cancellationToken)) return TableErrors.NumberTaken;

        // A number out of range is a broken domain invariant, and Create throws.
        // The API turns that into a 400.
        Table table = Table.Create(currentVenue.Id, command.Number, clock.GetUtcNow());

        await tables.AddAsync(table, cancellationToken);

        return TableSummary.Of(table);
    }
}
