using DrinkIt.Api.Common;
using DrinkIt.Application.Authentication;

namespace DrinkIt.Api.Tests.Common;

/// <summary>
/// The one place that decides what an Application error code looks like once it
/// reaches the wire as the RFC 9457 "type" member.
/// </summary>
public class ProblemTypesTests
{
    // Dots separate namespaces in a code and become urn segments; underscores
    // separate words and become hyphens, which is what reads as a URI.
    [Theory]
    [InlineData("auth.invalid_credentials", "urn:drinkit:problem:auth:invalid-credentials")]
    [InlineData("staff.username_length", "urn:drinkit:problem:staff:username-length")]
    [InlineData("conflict", "urn:drinkit:problem:conflict")]
    public void For_WhenGivenAnErrorCode_BuildsTheProblemUrn(string code, string expected) =>
        Assert.Equal(expected, ProblemTypes.For(code));

    /// <summary>
    /// The point of the whole convention: whatever comes out has to be an
    /// absolute URI, so it identifies the same problem type from every host
    /// instead of being resolved against whichever one answered.
    /// </summary>
    [Theory]
    [InlineData("auth.invalid_credentials")]
    [InlineData("staff.role_invalid")]
    [InlineData("order.illegal_transition")]
    public void For_ForAnyErrorCode_ProducesAnAbsoluteUri(string code)
    {
        string type = ProblemTypes.For(code);

        Assert.True(Uri.TryCreate(type, UriKind.Absolute, out Uri? uri), $"'{type}' is not an absolute URI.");
        Assert.Equal("urn", uri!.Scheme);
    }

    // RFC 9457 §3.1.1: when "type" is absent its value is assumed to be
    // "about:blank". Minting a urn for an error with no code would announce a
    // problem type that nothing defines.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void For_WhenThereIsNoCode_FallsBackToAboutBlank(string code) =>
        Assert.Equal("about:blank", ProblemTypes.For(code));

    // Ties the convention to a code that actually ships, so renaming it shows up
    // here as a failing test instead of silently changing the wire contract.
    [Fact]
    public void For_ForTheLoginError_MatchesThePublishedType() =>
        Assert.Equal(
            "urn:drinkit:problem:auth:invalid-credentials",
            ProblemTypes.For(LoginHandler.InvalidCredentials.Code));
}
