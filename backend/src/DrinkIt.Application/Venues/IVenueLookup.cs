namespace DrinkIt.Application.Venues;

public interface IVenueLookup
{
    /// <summary>
    /// Resolves the venue a customer reached through their QR. Venues carry no
    /// global query filter, so this works before any venue is known.
    /// </summary>
    Task<Guid?> FindIdBySlugAsync(string slug, CancellationToken cancellationToken);
}
