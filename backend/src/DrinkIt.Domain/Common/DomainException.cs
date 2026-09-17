namespace DrinkIt.Domain.Common;

/// <summary>
/// Thrown when a domain invariant is broken. Application-level errors that are
/// expected (a name already taken, a missing record) use Result instead.
/// </summary>
/// <remarks>
/// <see cref="Code"/> is the stable half and <see cref="Exception.Message"/> the
/// disposable one. Tests assert the code so that rewording a message does not
/// break them, and the API maps the code to the ProblemDetails type so the PWA
/// can branch on it and translate it.
///
/// The message reaches the client: the API answers a broken invariant with a
/// 400 whose detail is this text (ADR-0009). So it is written for the person
/// who sent the request, never for the log — it names no table, no column and
/// nothing about infrastructure, and it must not echo data the caller did not
/// send. A message that cannot promise that does not belong in this exception.
/// </remarks>
public sealed class DomainException(string code, string message) : Exception(message)
{
    /// <summary>Stable identifier, formatted as "aggregate.rule".</summary>
    public string Code { get; } = code;
}
