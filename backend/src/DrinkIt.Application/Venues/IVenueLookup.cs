namespace DrinkIt.Application.Venues;

/// <summary>
/// Who a venue is, for the two places that need it before any venue is known:
/// the middleware that resolves the tenant, and the customer's menu header.
/// </summary>
public sealed record VenueIdentity(Guid Id, string Name, string Slug);

public interface IVenueLookup
{
    /// <summary>
    /// Resolves the venue a customer reached through their QR. Venues carry no
    /// global query filter, so this works before any venue is known.
    /// </summary>
    Task<VenueIdentity?> FindBySlugAsync(string slug, CancellationToken cancellationToken);
}
