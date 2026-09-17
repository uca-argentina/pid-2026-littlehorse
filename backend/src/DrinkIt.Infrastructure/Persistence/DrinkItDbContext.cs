using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Persistence;

public sealed class DrinkItDbContext(DbContextOptions<DrinkItDbContext> options, ICurrentVenue currentVenue)
    : DbContext(options)
{
    public DbSet<Venue> Venues => Set<Venue>();

    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Order> Orders => Set<Order>();

    /// <summary>Where each venue's order codes are up to. Not a domain aggregate.</summary>
    internal DbSet<OrderCodeCounter> OrderCodeCounters => Set<OrderCodeCounter>();

    /// <summary>Read by the global query filters below.</summary>
    private Guid CurrentVenueId => currentVenue.Id;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DrinkItDbContext).Assembly);

        // Every aggregate that belongs to a venue is filtered here, in one place,
        // so no query anywhere has to remember to write the WHERE by hand.
        //
        // Venue itself is deliberately NOT filtered: it is the tenant root, and
        // resolving the tenant means looking a venue up by slug before any venue
        // is known. Filtering it would make that lookup impossible.
        modelBuilder.Entity<StaffUser>().HasQueryFilter(user => user.VenueId == CurrentVenueId);
        modelBuilder.Entity<Product>().HasQueryFilter(product => product.VenueId == CurrentVenueId);
        modelBuilder.Entity<Order>().HasQueryFilter(order => order.VenueId == CurrentVenueId);

        base.OnModelCreating(modelBuilder);
    }
}
