using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DrinkIt.Api.Common;

/// <summary>
/// Publishes every contract enum as a string with its names, so the PWA's
/// generated client can check each comparison against them.
/// </summary>
/// <remarks>
/// Needed because <see cref="StrictStringEnumConverter{TEnum}"/> is its own
/// converter, not System.Text.Json's — the one the OpenAPI generator knows how
/// to describe — and it has to be, to refuse a comma-separated list.
/// </remarks>
internal sealed class ContractEnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        Type type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;

        if (type.IsEnum && IsContractEnum(type))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = null;
            schema.Enum = [.. Enum.GetNames(type).Select(name => (JsonNode)JsonValue.Create(name))];
        }

        return Task.CompletedTask;
    }

    private static bool IsContractEnum(Type type) =>
        type.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType is { IsGenericType: true } converter
        && converter.GetGenericTypeDefinition() == typeof(StrictStringEnumConverter<>);
}
