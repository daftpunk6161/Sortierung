using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Romulus.Api;

public static class OpenApiSpec
{
    public const string DocumentName = "v1";
    private const string ApiKeySchemeId = "ApiKey";

    public static void Configure(OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;

        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Romulus API",
                Version = Program.ApiVersion,
                Description = "Romulus REST API — Your Collection, Perfected. Region deduplication, junk removal, format conversion."
            };

            document.Servers = new List<OpenApiServer>
            {
                new() { Url = "http://127.0.0.1:7878" }
            };

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
            document.Components.SecuritySchemes[ApiKeySchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Api-Key",
                Description = "API key required for authenticated endpoints."
            };

            var securitySchemeReference = new OpenApiSecuritySchemeReference(ApiKeySchemeId, document, externalResource: null);

            document.Security = new List<OpenApiSecurityRequirement>
            {
                new()
                {
                    [securitySchemeReference] = new List<string>()
                }
            };

            return Task.CompletedTask;
        });

        options.AddSchemaTransformer((schema, context, _) =>
        {
            ApplyPrimitiveOpenApiType(schema, context);
            return Task.CompletedTask;
        });

        options.AddOperationTransformer(async (operation, context, cancellationToken) =>
        {
            var relativePath = NormalizeRelativePath(context.Description.RelativePath);
            var httpMethod = context.Description.HttpMethod;

            if (string.Equals(relativePath, "/healthz", StringComparison.OrdinalIgnoreCase))
            {
                operation.Security = new List<OpenApiSecurityRequirement>();
                return;
            }

            if (!string.Equals(relativePath, "/openapi", StringComparison.OrdinalIgnoreCase))
            {
                AddHeaderParameterIfMissing(
                    operation,
                    "X-Client-Id",
                    required: false,
                    "Optional logical client binding used for run ownership checks.");
            }

            if (string.Equals(relativePath, "/runs", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(httpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new()
                        {
                            Schema = await context.GetOrCreateSchemaAsync(typeof(RunRequest), parameterDescription: null, cancellationToken)
                        }
                    }
                };

                AddHeaderParameterIfMissing(
                    operation,
                    "X-Idempotency-Key",
                    required: false,
                    "Reuse the same run for retries of the same request.");
            }

            if (string.Equals(relativePath, "/runs/{runId}/reviews/approve", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(httpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = false,
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new()
                        {
                            Schema = await context.GetOrCreateSchemaAsync(typeof(ApiReviewApprovalRequest), parameterDescription: null, cancellationToken)
                        }
                    }
                };
            }
        });
    }

    private static void ApplyPrimitiveOpenApiType(OpenApiSchema schema, OpenApiSchemaTransformerContext context)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (schema.Type is not null)
            return;

        var targetType = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (targetType.IsEnum)
            return;

        if (targetType == typeof(bool))
        {
            schema.Type = JsonSchemaType.Boolean;
            return;
        }

        if (targetType == typeof(byte) ||
            targetType == typeof(short) ||
            targetType == typeof(int) ||
            targetType == typeof(long) ||
            targetType == typeof(sbyte) ||
            targetType == typeof(ushort) ||
            targetType == typeof(uint) ||
            targetType == typeof(ulong))
        {
            schema.Type = JsonSchemaType.Integer;
            return;
        }

        if (targetType == typeof(float) ||
            targetType == typeof(double) ||
            targetType == typeof(decimal))
        {
            schema.Type = JsonSchemaType.Number;
            return;
        }

        if (targetType == typeof(char) ||
            targetType == typeof(string) ||
            targetType == typeof(Guid) ||
            targetType == typeof(Uri) ||
            targetType == typeof(DateOnly) ||
            targetType == typeof(TimeOnly) ||
            targetType == typeof(DateTime) ||
            targetType == typeof(DateTimeOffset) ||
            targetType == typeof(TimeSpan))
        {
            schema.Type = JsonSchemaType.String;
        }
    }

    private static string NormalizeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "/";

        var trimmed = relativePath.TrimStart('/');
        var querySeparatorIndex = trimmed.IndexOf('?', StringComparison.Ordinal);
        if (querySeparatorIndex >= 0)
            trimmed = trimmed[..querySeparatorIndex];

        return "/" + trimmed;
    }

    private static void AddHeaderParameterIfMissing(
        OpenApiOperation operation,
        string name,
        bool required,
        string description)
    {
        if (operation.Parameters is null)
            operation.Parameters = new List<IOpenApiParameter>();
        if (operation.Parameters.Any(parameter =>
                parameter.In == ParameterLocation.Header &&
                string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
            Description = description,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String
            }
        });
    }
}
