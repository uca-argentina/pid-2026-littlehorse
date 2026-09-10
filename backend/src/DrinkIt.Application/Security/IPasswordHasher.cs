namespace DrinkIt.Application.Security;

/// <summary>
/// Hashing lives behind a port so the algorithm can be replaced without
/// touching a single use case, and so Application tests never pay the cost of
/// a real key-derivation function.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}
