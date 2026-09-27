using DrinkIt.Application.Common;
using DrinkIt.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DrinkIt.Infrastructure.Persistence;

/// <summary>
/// US-30: stamps when and by whom in the one place every save goes through, so
/// no use case can forget it and no test has to set it. Registered on the real
/// DbContext only; a test that wants the stamp adds it, and one that does not
/// gets none.
/// </summary>
/// <remarks>
/// What it does not see is what does not go through SaveChanges: the stock a
/// sale takes is a statement of its own (ExecuteUpdate), and a drink being sold
/// is not somebody editing the product, so the mark stays where it was.
/// </remarks>
internal sealed class AuditInterceptor(TimeProvider clock, ICurrentStaffUser user) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);

        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        DateTimeOffset now = clock.GetUtcNow();
        string? username = user.Username;

        // Entries() detects the changes first, so what is Modified here is what
        // is about to be written: a save with nothing to write moves no mark.
        foreach (EntityEntry entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                // Filled only when empty: a category decides its own creation
                // time, because that is what orders the tabs.
                if (entry.Entity is IHasCreationTime && entry.Property(nameof(IHasCreationTime.CreatedAt)).CurrentValue is null)
                    entry.Property(nameof(IHasCreationTime.CreatedAt)).CurrentValue = now;

                if (entry.Entity is IAudited) entry.Property(nameof(IAudited.CreatedBy)).CurrentValue = username;
            }
            else if (entry.State == EntityState.Modified && entry.Entity is IAudited)
            {
                entry.Property(nameof(IAudited.LastModifiedAt)).CurrentValue = now;
                entry.Property(nameof(IAudited.LastModifiedBy)).CurrentValue = username;
            }
        }
    }
}
