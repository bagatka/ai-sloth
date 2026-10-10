using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Typed IDs travel as UUID strings (<see cref="TypedIdJsonConverter{T}"/>), and the API's document
/// says so, so clients generated from it type them as strings rather than as anything.
/// </summary>
internal static class OpenApiTypedIds
{
    public static Task DescribeAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        // A nullable ID shares its type's schema, and is null where a property says so.
        Type type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        bool typedId = type.GetInterfaces().Any(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ITypedId<>));
        if (typedId)
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "uuid";
        }

        return Task.CompletedTask;
    }
}
