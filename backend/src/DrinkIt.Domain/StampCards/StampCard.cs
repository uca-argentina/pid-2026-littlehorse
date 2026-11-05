using System.Security.Cryptography;
using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.StampCards;

/// <summary>
/// The card a phone carries (US-58): no account, only a long secret kept in the
/// phone. Stamps are not stored here but derived from the orders that carry
/// this card, so undoing a delivery needs no compensation.
/// </summary>
public sealed class StampCard : CreationStamp, IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "stamp_card.venue_required";
    }

    public const int TokenLength = 64;

    private StampCard(Guid id, Guid venueId, string token)
    {
        Id = id;
        VenueId = venueId;
        Token = token;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public string Token { get; }

    public int RewardsClaimed { get; private set; }

    public static StampCard Issue(Guid venueId)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "A stamp card must belong to a venue.");

        // The system's cryptographic source, like TrackingToken: this secret
        // is the whole identity of whoever holds the card.
        return new StampCard(Guid.CreateVersion7(), venueId, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenLength / 2)));
    }

    /// <summary>What arrives from a phone may be anything; a wrong shape is an answer, not a bug.</summary>
    public static bool IsWellShaped(string? token) =>
        token is { Length: TokenLength } && token.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    /// <summary>
    /// A reward was taken with this card (US-59). The balance is derived from
    /// orders, so this only leaves a mark on the row: the concurrency token
    /// reads it, and of two phones claiming at once exactly one can write it.
    /// </summary>
    public void Claim() => RewardsClaimed++;
}
