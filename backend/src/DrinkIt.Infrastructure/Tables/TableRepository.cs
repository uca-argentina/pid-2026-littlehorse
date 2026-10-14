using DrinkIt.Application.Tables;
using DrinkIt.Domain.Tables;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Tables;

internal sealed class TableRepository(DrinkItDbContext context) : ITableRepository
{
    /// <summary>
    /// No venue in the WHERE: the global query filter adds the one the request
    /// resolved. No <c>IsActive</c> either, on purpose: a put-away table keeps
    /// its number, same as the unique index it has to agree with.
    /// </summary>
    public Task<bool> NumberExistsAsync(int number, CancellationToken cancellationToken) =>
        context.Tables.AnyAsync(table => table.Number == number, cancellationToken);

    /// <summary>The venue filter applies here too: another venue's id finds nothing.</summary>
    public Task<Table?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        context.Tables.SingleOrDefaultAsync(table => table.Id == id, cancellationToken);

    public async Task AddAsync(Table table, CancellationToken cancellationToken)
    {
        context.Tables.Add(table);

        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
