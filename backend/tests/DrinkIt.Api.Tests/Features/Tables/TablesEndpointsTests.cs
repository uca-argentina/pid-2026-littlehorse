using DrinkIt.Api.Features.Tables;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Tables;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Tables;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Tables;

public class TablesEndpointsTests
{
    private const string Path = "/staff/tables";

    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static Table ATable(int number = 5) => Table.Create(TheVenue, number, 8, null, DateTimeOffset.UtcNow);

    // Pins the success shape: the Angular client is generated from it, so
    // renaming a property here breaks the screen silently.
    [Fact]
    public async Task CreateAsync_WhenTheNumberIsFree_RespondsWithTheCreatedTable()
    {
        CreateTableHandler handler = new(new FakeTables(), new Fake.CurrentVenue(), TimeProvider.System);

        IResult result = await TablesEndpoints.CreateAsync(new CreateTableRequest(5, 10, "Next to the DJ booth"), handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Post);

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, response.Body.GetProperty("id").GetGuid());
        Assert.Equal(5, response.Body.GetProperty("number").GetInt32());
        Assert.Equal(10, response.Body.GetProperty("capacity").GetInt32());
        Assert.Equal("Next to the DJ booth", response.Text("location"));
        Assert.True(response.Body.GetProperty("isActive").GetBoolean());
        Assert.True(response.Body.TryGetProperty("audit", out _));
    }

    // A taken number is a conflict with the venue's tables, and the screen tells
    // it apart from a malformed one without reading the message.
    [Fact]
    public async Task CreateAsync_WhenTheNumberIsAlreadyUsedInThisVenue_RespondsWithConflict()
    {
        CreateTableHandler handler = new(new FakeTables(taken: 5), new Fake.CurrentVenue(), TimeProvider.System);

        IResult result = await TablesEndpoints.CreateAsync(new CreateTableRequest(5, 8, null), handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:drinkit:problem:table:number-taken", response.Text("type"));
    }

    // The endpoint does not check the number itself: the domain throws and the
    // global handler answers. This pins that the exception gets out untouched.
    [Fact]
    public async Task CreateAsync_WhenTheNumberIsOutOfRange_LetsTheDomainExceptionThrough()
    {
        CreateTableHandler handler = new(new FakeTables(), new Fake.CurrentVenue(), TimeProvider.System);

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => TablesEndpoints.CreateAsync(new CreateTableRequest(0, 8, null), handler, CancellationToken.None));

        Assert.Equal(Table.ErrorCodes.NumberOutOfRange, error.Code);
    }

    [Fact]
    public async Task ListAsync_WhenTheVenueHasTables_RespondsWithAllOfThemPutAwayOnesIncluded()
    {
        FakeTableQueries queries = new(
            new TableSummary(Guid.CreateVersion7(), 2, 8, null, true, new AuditInfo(null, null, null, null)),
            new TableSummary(Guid.CreateVersion7(), 7, 8, null, false, new AuditInfo(null, null, null, null)));

        IResult result = await TablesEndpoints.ListAsync(queries, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(2, response.Body.GetArrayLength());
        Assert.Equal(2, response.Body[0].GetProperty("number").GetInt32());
        Assert.True(response.Body[0].GetProperty("isActive").GetBoolean());
        Assert.False(response.Body[1].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task DeactivateAsync_WhenTheTableExists_RespondsWithItPutAway()
    {
        Table table = ATable();

        IResult result = await TablesEndpoints.DeactivateAsync(
            table.Id, new DeactivateTableHandler(new FakeTables(existing: table)), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{table.Id}/deactivate", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(5, response.Body.GetProperty("number").GetInt32());
        Assert.False(response.Body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task DeactivateAsync_WhenTheVenueHasNoSuchTable_RespondsNotFound()
    {
        Guid id = Guid.CreateVersion7();

        IResult result = await TablesEndpoints.DeactivateAsync(
            id, new DeactivateTableHandler(new FakeTables()), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{id}/deactivate", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:table:not-found", response.Text("type"));
    }

    [Fact]
    public async Task ReactivateAsync_WhenTheTableWasPutAway_RespondsWithItBack()
    {
        Table table = ATable();
        table.Deactivate();

        IResult result = await TablesEndpoints.ReactivateAsync(
            table.Id, new ReactivateTableHandler(new FakeTables(existing: table)), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{table.Id}/reactivate", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.True(response.Body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task ReactivateAsync_WhenTheVenueHasNoSuchTable_RespondsNotFound()
    {
        Guid id = Guid.CreateVersion7();

        IResult result = await TablesEndpoints.ReactivateAsync(
            id, new ReactivateTableHandler(new FakeTables()), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{id}/reactivate", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:table:not-found", response.Text("type"));
    }

    private static class Fake
    {
        public sealed class CurrentVenue : ICurrentVenue
        {
            public Guid Id => TheVenue;
        }
    }
}
