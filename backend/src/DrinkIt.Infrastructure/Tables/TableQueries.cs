using DrinkIt.Application.Common;
using DrinkIt.Application.Tables;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Tables;

/// <summary>Read side: no repository, no tracking, a projection straight to the DTO.</summary>
internal sealed class TableQueries(DrinkItDbContext context) : ITableQueries
{
    public async Task<IReadOnlyList<TableSummary>> ListAsync(CancellationToken cancellationToken) =>
        await context.Tables
            .AsNoTracking()
            .OrderBy(table => table.Number)
            .Select(table => new TableSummary(table.Id, table.Number, table.IsActive, new AuditInfo(table.CreatedAt, table.CreatedBy, table.LastModifiedAt, table.LastModifiedBy)))
            .ToListAsync(cancellationToken);
}
