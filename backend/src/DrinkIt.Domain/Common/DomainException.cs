namespace DrinkIt.Domain.Common;

/// <summary>
/// Thrown when a domain invariant is broken. Application-level errors that are
/// expected (a name already taken, a missing record) use Result instead.
/// </summary>
/// <remarks>
/// <see cref="Code"/> is the stable half and <see cref="Exception.Message"/> the
/// disposable one. Tests assert the code so that rewording a message does not
/// break them, and the API maps the code to the ProblemDetails type so the PWA
/// can branch on it and translate it. The message is for logs and developers.
/// </remarks>
public sealed class DomainException(string code, string message) : Exception(message)
{
    /// <summary>Stable identifier, formatted as "aggregate.rule".</summary>
    public string Code { get; } = code;
}
