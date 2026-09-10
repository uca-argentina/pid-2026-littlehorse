using DrinkIt.Infrastructure;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.Extensions;

public static class MigrationExtensions
{
    extension(WebApplication app)
    {
        /// <summary>
        /// Applies any pending EF Core migrations. The caller decides whether
        /// this is safe to run — see Program.cs, which only calls it in
        /// Development. Running this against a real deployment belongs in a
        /// deploy step with a backup and a maintenance window, never here.
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
