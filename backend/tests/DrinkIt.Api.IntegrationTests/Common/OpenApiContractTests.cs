using System.Text.Json;
using System.Text.RegularExpressions;
using DrinkIt.Api.IntegrationTests.Persistence;

namespace DrinkIt.Api.IntegrationTests.Common;

/// <summary>
/// The contract is public: the PWA's client is generated from it, and anybody
/// can read it. What it describes has to be what the API does, not how the
/// team got there — those notes belong in remarks, which it never publishes.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed partial class OpenApiContractTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task OpenApi_Always_DescribesWithoutTheTeamsNotes()
    {
        using HttpClient client = _factory.CreateClient();
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        string[] leaking =
        [
            .. TextsIn(document.RootElement)
                .Where(text => TeamNote().IsMatch(text))
                .Distinct()
                .Order(StringComparer.Ordinal)
        ];

        Assert.True(
            leaking.Length == 0,
            $"These descriptions quote the team's notes:{Environment.NewLine}{string.Join(Environment.NewLine, leaking)}");
    }

    // The PWA's client is generated from this: a field published as a plain
    // string leaves every comparison against it unchecked by the compiler.
    [Theory]
    [InlineData("LoginResponse", "role", "Administrator,Kds,Waiter")]
    [InlineData("StaffUserResponse", "role", "Administrator,Kds,Waiter")]
    [InlineData("CreateStaffUserRequest", "role", "Administrator,Kds,Waiter")]
    [InlineData("ChangeStaffUserRoleRequest", "role", "Administrator,Kds,Waiter")]
    [InlineData("ConfirmedOrderResponse", "status", "AwaitingPayment,Paid,Queued,InPreparation,Ready,Delivered,Canceled")]
    [InlineData("TrackedOrderResponse", "status", "AwaitingPayment,Paid,Queued,InPreparation,Ready,Delivered,Canceled")]
    [InlineData("KdsQueueOrderResponse", "status", "Queued,InPreparation,Ready")]
    public async Task OpenApi_Always_PublishesEachEnumByItsNames(string schema, string property, string names)
    {
        using HttpClient client = _factory.CreateClient();
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        JsonElement field = Resolve(schemas, schemas.GetProperty(schema).GetProperty("properties").GetProperty(property));

        Assert.True(field.TryGetProperty("enum", out JsonElement values), $"{schema}.{property} is not published as an enum.");
        Assert.Equal(names.Split(','), values.EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>Follows a $ref to the schema it names, the way a generator would.</summary>
    private static JsonElement Resolve(JsonElement schemas, JsonElement field) =>
        field.TryGetProperty("$ref", out JsonElement reference)
            ? schemas.GetProperty(reference.GetString()!.Split('/')[^1])
            : field;

    /// <summary>Every description and summary, wherever it sits in the document.</summary>
    private static IEnumerable<string> TextsIn(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    bool isText = property.Name is "description" or "summary";

                    if (isText && property.Value.ValueKind == JsonValueKind.String) yield return property.Value.GetString()!;

                    foreach (string text in TextsIn(property.Value)) yield return text;
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (string text in TextsIn(item)) yield return text;
                }

                break;
        }
    }

    /// <summary>A story, an ADR, the team's own guide, or the date somebody decided something.</summary>
    [GeneratedRegex(@"\bUS-\d+|\bADR\b|ADR-\d+|CLAUDE\.md|\b20\d\d-\d\d-\d\d\b")]
    private static partial Regex TeamNote();
}
