using DrinkIt.Api.Features.Menu;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Menu;

public class CategoriesEndpointsTests
{
    private const string Path = "/staff/categories";

    // Pins the success shape: the Angular client is generated from it, so
    // renaming a property here breaks the screen silently.
    [Fact]
    public async Task CreateAsync_WhenTheNameIsFree_RespondsWithTheCreatedCategory()
    {
        HttpResponseSnapshot response = await Create(new CreateCategoryRequest("Cervezas"));

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, response.Body.GetProperty("id").GetGuid());
        Assert.Equal("Cervezas", response.Text("name"));
    }

    // A taken name is a conflict with the menu, and the screen tells it apart
    // from a malformed one without reading the message.
    [Fact]
    public async Task CreateAsync_WhenTheNameIsAlreadyUsedInThisVenue_RespondsWithConflict()
    {
        HttpResponseSnapshot response = await Create(new CreateCategoryRequest("Cervezas"), taken: "Cervezas");

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:drinkit:problem:category:name-taken", response.Text("type"));
    }

    // The endpoint does not check the name itself: the domain throws and the
    // global handler answers. This pins that the exception gets out untouched.
    [Fact]
    public async Task CreateAsync_WhenTheNameIsBlank_LetsTheDomainExceptionThrough()
    {
        DomainException error = await Assert.ThrowsAsync<DomainException>(() => Create(new CreateCategoryRequest("   ")));

        Assert.Equal(Category.ErrorCodes.NameRequired, error.Code);
    }

    [Fact]
    public async Task ListAsync_WhenTheVenueHasCategories_RespondsWithAllOfThemInOrder()
    {
        FakeCategoryQueries queries = new(
            new CategoryListItem(Guid.CreateVersion7(), "Tragos"),
            new CategoryListItem(Guid.CreateVersion7(), "Cervezas"));

        IResult result = await CategoriesEndpoints.ListAsync(queries, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(2, response.Body.GetArrayLength());
        Assert.Equal("Tragos", response.Body[0].GetProperty("name").GetString());
        Assert.Equal("Cervezas", response.Body[1].GetProperty("name").GetString());
        Assert.NotEqual(Guid.Empty, response.Body[0].GetProperty("id").GetGuid());
    }

    private static async Task<HttpResponseSnapshot> Create(CreateCategoryRequest request, string? taken = null)
    {
        CreateCategoryHandler handler = new(new FakeCategories(taken), new Fake.CurrentVenue(), TimeProvider.System);

        IResult result = await CategoriesEndpoints.CreateAsync(request, handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, Path, HttpMethods.Post);
    }

    private static class Fake
    {
        public sealed class CurrentVenue : ICurrentVenue
        {
            public Guid Id { get; } = Guid.CreateVersion7();
        }
    }
}
