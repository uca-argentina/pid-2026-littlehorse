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

    public async Task<bool> TryAddAsync(Table table, CancellationToken cancellationToken)
    {
        context.Tables.Add(table);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException)
        {
            // A unique index refused the insert. If the number is taken now,
            // another administrator saved it after our check, and that is the
            // answer. Any other index means something we have not thought
            // about, and swallowing it would hide it.
            context.ChangeTracker.Clear();

            if (await NumberExistsAsync(table.Number, cancellationToken)) return false;

            throw;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
