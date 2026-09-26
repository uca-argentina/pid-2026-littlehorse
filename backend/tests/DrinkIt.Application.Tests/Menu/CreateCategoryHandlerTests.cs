using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Tests.Menu;

public class CreateCategoryHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WhenTheNameIsFree_AddsTheCategoryToTheVenueOfTheSignedInAdministrator()
    {
        FakeCategories categories = new();

        Result<CategorySummary> result = await HandlerOver(categories).HandleAsync(
            new CreateCategoryCommand("Cervezas"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TheVenue, categories.Added!.VenueId);
        Assert.Equal("Cervezas", categories.Added.Name);
        Assert.Equal(Now, categories.Added.CreatedAt);
        Assert.Equal(categories.Added.Id, result.Value.Id);
        Assert.Equal("Cervezas", result.Value.Name);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNameIsAlreadyUsedInThisVenue_Fails()
    {
        FakeCategories categories = new(taken: "Cervezas");

        Result<CategorySummary> result = await HandlerOver(categories).HandleAsync(
            new CreateCategoryCommand("Cervezas"), CancellationToken.None);

        Assert.Equal(CreateCategoryHandler.NameTaken, result.Error);
        Assert.Null(categories.Added);
    }

    // Trimmed before the check, so "Cervezas " cannot get past it and collide
    // on the unique index afterwards, where the failure is a 500.
    [Theory]
    [InlineData("cervezas")]
    [InlineData("  Cervezas  ")]
    public async Task HandleAsync_WhenTheTakenNameIsTypedWithOtherCasingOrPadding_StillFails(string name)
    {
        FakeCategories categories = new(taken: "Cervezas");

        Result<CategorySummary> result = await HandlerOver(categories).HandleAsync(
            new CreateCategoryCommand(name), CancellationToken.None);

        Assert.Equal(CreateCategoryHandler.NameTaken, result.Error);
    }

    // A broken invariant is not an expected outcome: the domain throws, the API
    // turns it into a 400, and the handler does not restate the rules.
    [Fact]
    public async Task HandleAsync_WhenTheDomainRejectsTheName_ThrowsWithoutCreatingAnything()
    {
        FakeCategories categories = new();

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => HandlerOver(categories).HandleAsync(new CreateCategoryCommand("   "), CancellationToken.None));

        Assert.Equal(Category.ErrorCodes.NameRequired, error.Code);
        Assert.Null(categories.Added);
    }

    private static CreateCategoryHandler HandlerOver(FakeCategories categories) =>
        new(categories, new Fake.CurrentVenue(), new Fake.Clock());

    private static class Fake
    {
        public sealed class CurrentVenue : ICurrentVenue
        {
            public Guid Id => TheVenue;
        }

        public sealed class Clock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
