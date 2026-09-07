using DrinkIt.Infrastructure.Security;

namespace DrinkIt.Infrastructure.Tests.Security;

public class IdentityPasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void Verify_WhenPasswordIsCorrect_ReturnsTrue()
    {
        string hash = _hasher.Hash("a long and decent password");

        Assert.True(_hasher.Verify("a long and decent password", hash));
    }

    [Fact]
    public void Verify_WhenPasswordIsWrong_ReturnsFalse()
    {
        string hash = _hasher.Hash("a long and decent password");

        Assert.False(_hasher.Verify("a long and wrong password", hash));
    }

    // The one that actually catches a bad implementation. A plain SHA-256 of the
    // password passes both tests above and fails this one, because without a
    // per-password salt the same input always produces the same digest — which is
    // what makes rainbow tables work.
    [Fact]
    public void Hash_WhenCalledTwiceWithTheSamePassword_ProducesDifferentHashes()
    {
        string first = _hasher.Hash("a long and decent password");
        string second = _hasher.Hash("a long and decent password");

        Assert.NotEqual(first, second);
    }
}
