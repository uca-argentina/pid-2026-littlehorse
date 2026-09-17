using DrinkIt.Infrastructure;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.Extensions;

public static class MigrationExtensions
{
    extension(WebApplication app)
    {
        /// <summary>
        /// Applies any pending EF Core migrations. Program.cs calls this on every
        /// startup, in every environment: a single venue with low traffic doesn't
        /// justify a separate deploy step, and Azure SQL's free-tier backups cover
        /// the rollback case. minReplicas 0 / maxReplicas 2 (see infra/main.bicep)
        /// means two replicas can race to migrate on a cold start that scales past
        /// one instance; EF Core's migration history table serializes that via a
        /// database lock, so the loser waits instead of corrupting the schema.
        /// </summary>
        public async Task ApplyMigrationsAsync()
        {
            using IServiceScope scope = app.Services.CreateScope();
            DrinkItDbContext context = scope.ServiceProvider.GetRequiredService<DrinkItDbContext>();
            await context.Database.MigrateAsync();
        }

        /// <summary>
        /// Safe to call on every restart. The seeder itself is internal to
        /// Infrastructure, so this goes through the same public door DI does.
        /// </summary>
        public async Task SeedDevelopmentDataAsync()
        {
            using IServiceScope scope = app.Services.CreateScope();
            await scope.ServiceProvider.SeedDevelopmentDataAsync(CancellationToken.None);
        }
    }
}
