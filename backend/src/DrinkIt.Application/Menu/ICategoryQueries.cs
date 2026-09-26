namespace DrinkIt.Application.Menu;

/// <summary>One category as a screen lists it: what to show and what to send back.</summary>
public sealed record CategoryListItem(Guid Id, string Name);

/// <summary>The read side. No repository here: reads have no invariant to protect.</summary>
public interface ICategoryQueries
{
    /// <summary>
    /// Every category of the venue of the current request, the first one created
    /// first: that is the order the customer's tabs are drawn in.
    /// </summary>
    Task<IReadOnlyList<CategoryListItem>> ListAsync(CancellationToken cancellationToken);
}
