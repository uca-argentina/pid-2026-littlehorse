namespace DrinkIt.Application.Common;

/// <summary>
/// Expected failures travel as values, not exceptions. Exceptions stay for
/// broken domain invariants, which are bugs rather than outcomes. See CLAUDE.md.
/// </summary>
public sealed class Result<TValue>
{
    private readonly TValue? _value;

    private Result(TValue value)
    {
        _value = value;
        Error = null;
    }

    private Result(Error error)
    {
        _value = default;
        Error = error;
    }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    /// <summary>Throws when the result failed, so a missed check fails loudly.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error!.Code}).");

    // Implicit conversions instead of static factories: a handler writes
    // "return user;" or "return SomeError;" and the compiler infers the rest.
    public static implicit operator Result<TValue>(TValue value) => new(value);

    public static implicit operator Result<TValue>(Error error) => new(error);
}
