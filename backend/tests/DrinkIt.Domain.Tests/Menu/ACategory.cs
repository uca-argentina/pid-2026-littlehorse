namespace DrinkIt.Domain.Tests.Menu;

/// <summary>
/// Two category ids for the tests that only need "some category" and "another
/// one". The domain holds the id and nothing else about it: whether it exists
/// in the venue is the handler's question.
/// </summary>
internal static class ACategory
{
    public static readonly Guid Id = Guid.CreateVersion7();

    public static readonly Guid Other = Guid.CreateVersion7();
}
