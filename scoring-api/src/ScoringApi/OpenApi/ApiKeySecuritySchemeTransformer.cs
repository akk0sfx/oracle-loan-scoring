using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using ScoringApi.Infrastructure;

namespace ScoringApi.OpenApi;

/// <summary>Declares the X-Api-Key scheme and applies it to /api/v1/* operations in /openapi/v1.json.</summary>
internal sealed class ApiKeySecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeId = "ApiKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyEndpointFilter.HeaderName,
            Description = "API key shared with Creatio (system setting UsrScoringApiKey).",
        };

        foreach (var (path, item) in document.Paths)
        {
            if (!path.StartsWith("/api/v1/", StringComparison.Ordinal) || item.Operations is null)
            {
                continue;
            }

            foreach (var operation in item.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SchemeId, document)] = [],
                });
            }
        }

        return Task.CompletedTask;
    }
}
