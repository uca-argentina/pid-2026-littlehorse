namespace DrinkIt.Api.Common;

/// <summary>
/// Turns an Application error code into the "type" member of a problem
/// response. The single place that decides what those identifiers look like.
/// </summary>
/// <remarks>
/// RFC 9457 §3.1.1 defines "type" as a URI reference and resolves a relative one
/// against the document's base URI. A bare code such as
/// "auth.invalid_credentials" parses as a relative reference, so nothing rejects
/// it — it simply means something different depending on which host answered,
/// which is the opposite of the stable identifier the PWA branches on.
///
/// A urn rather than an https URL on purpose: a URL commits us to a domain we
/// would then have to keep serving, and RFC 9457 is explicit that a consumer
/// must not assume the type URI can be dereferenced. Switching to
/// "https://.../problems/..." later is a change to <see cref="Prefix"/> and to
/// the separator below, and nothing else.
/// </remarks>
internal static class ProblemTypes
{
    private const string Prefix = "urn:drinkit:problem:";

    /// <summary>
    /// What RFC 9457 §3.1.1 says "type" means when it is absent. Minting a urn
    /// for an error that carries no code would announce a problem type that
    /// nothing defines.
    /// </summary>
    private const string Unclassified = "about:blank";

    /// <summary>
    /// Whether this identifier was minted here. Lets a later stage tell a
    /// problem the endpoint already named from one it still has to name,
    /// without depending on which of the two ran first.
    /// </summary>
    public static bool Owns(string? type) => type?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// Dots separate namespaces in an error code and become urn segments;
    /// underscores separate words and become hyphens.
    /// </summary>
    public static string For(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode)) return Unclassified;

        return Prefix + errorCode.Trim().ToLowerInvariant().Replace('.', ':').Replace('_', '-');
    }
}
