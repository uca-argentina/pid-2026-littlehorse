using DrinkIt.Application.Common;

namespace DrinkIt.Application.Tests.Kds;

/// <summary>The bars the board's specs place orders in (US-36).</summary>
internal static class TestBars
{
    /// <summary>The tablet signed in on every spec, unless one says otherwise.</summary>
    public static readonly Guid Main = Guid.CreateVersion7();

    /// <summary>Another bar of the same night, whose orders the main one never sees.</summary>
    public static readonly Guid Other = Guid.CreateVersion7();
}

/// <summary>The KDS account signed in on the tablet, as its token says.</summary>
internal sealed class TheKdsIs(Guid id) : ICurrentStaffUser
{
    public static readonly TheKdsIs Main = new(TestBars.Main);

    public string? Username => "barra.pista";

    public Guid? Id => id;
}
