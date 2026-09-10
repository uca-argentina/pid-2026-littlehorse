using System.Diagnostics.CodeAnalysis;

namespace DrinkIt.Application.Common;

/// <summary>
/// An expected failure. <see cref="Code"/> is the stable half that the API maps
/// to the ProblemDetails type and the PWA branches on; the message is for logs
/// and developers, and rewording it breaks nothing.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "CA1716 protects consumers writing VB or F#, which only matters for a "
        + "published library. This is an application assembly, and Error is the idiomatic "
        + "name for this half of the Result pattern.")]
public sealed record Error(string Code, string Message);
