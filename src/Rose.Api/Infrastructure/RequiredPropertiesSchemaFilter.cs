using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Rose.Api.Infrastructure;

/// <summary>
/// Marks every non-nullable property as required, so generated TypeScript types say
/// <c>total: number</c> instead of <c>total?: number</c>.
/// </summary>
internal sealed class RequiredPropertiesSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null) return;
        foreach (var (name, property) in schema.Properties)
            if (!property.Nullable) schema.Required.Add(name);
    }
}