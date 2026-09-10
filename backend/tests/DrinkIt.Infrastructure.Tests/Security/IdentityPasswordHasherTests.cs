using System.Buffers.Binary;
using DrinkIt.Infrastructure.Security;

namespace DrinkIt.Infrastructure.Tests.Security;

public class IdentityPasswordHasherTests
{
    private const byte FormatMarkerV3 = 0x01;

    private const int PrfOffset = 1;

    private const int IterationCountOffset = 5;

    /// <summary>The PRF enum value Identity writes for HMAC-SHA512.</summary>
    private const uint HmacSha512 = 2;

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

    // The work factor is the whole point of a password hash, and it is the one
    // property the tests above cannot see: a hasher tuned down to a single
    // iteration passes all three. Identity's hash is self-describing, so the
    // parameters can be read back out of it.
    [Fact]
    public void Hash_WhenCalled_UsesTheIterationCountWeConfigured()
    {
        byte[] hash = HashOfADecentPassword();

        Assert.Equal(210_000u, BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(IterationCountOffset)));
    }

    [Fact]
    public void Hash_WhenCalled_DerivesWithHmacSha512()
    {
        byte[] hash = HashOfADecentPassword();

        Assert.Equal(HmacSha512, BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(PrfOffset)));
    }

    /// <summary>
    /// Identity's V3 payload is a 0x01 marker followed by the PRF, the iteration
    /// count and the salt length, each a big-endian uint32. Asserting the marker
    /// here keeps the offsets below honest: a future format would shift them.
    /// </summary>
    private byte[] HashOfADecentPassword()
    {
        byte[] hash = Convert.FromBase64String(_hasher.Hash("a long and decent password"));

        Assert.Equal(FormatMarkerV3, hash[0]);

        return hash;
    }
}
