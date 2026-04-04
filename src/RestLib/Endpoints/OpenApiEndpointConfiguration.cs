using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using RestLib.Configuration;

namespace RestLib.Endpoints;

/// <summary>
/// Configures OpenAPI metadata and endpoint conventions (auth, rate limiting) for RestLib endpoints.
/// Extracted from <see cref="RestLibEndpointExtensions"/> to keep endpoint registration logic separate
/// from OpenAPI schema configuration.
/// </summary>
internal static class OpenApiEndpointConfiguration
{
    /// <summary>
    /// Base configuration applied to all endpoints (auth, rate limiting, tags, summary, description).
    /// </summary>
    internal static void ConfigureEndpointBase<TEntity, TKey>(
        RouteHandlerBuilder endpoint,
        RestLibOperation operation,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        string entityName,
        string endpointNamePrefix,
        string operationId,
        string defaultSummary,
        string defaultDescription)
        where TEntity : class
    {
        var openApiConfig = config.OpenApi;

        // Use custom tag or fall back to the base entity type name (without registration suffix)
        var tag = string.IsNullOrEmpty(openApiConfig.Tag) ? typeof(TEntity).Name : openApiConfig.Tag;

        // Use custom summary or fall back to default
        var summary = openApiConfig.Summaries.GetSummary(operation) ?? defaultSummary;

        // Use custom description or fall back to default, append deprecation message if applicable
        var description = openApiConfig.Descriptions.GetDescription(operation) ?? defaultDescription;
        if (openApiConfig.Deprecated && !string.IsNullOrEmpty(openApiConfig.DeprecationMessage))
        {
            description = $"**DEPRECATED:** {openApiConfig.DeprecationMessage}\n\n{description}";
        }
        else if (openApiConfig.Deprecated)
        {
            description = $"**DEPRECATED:** This endpoint is deprecated and may be removed in a future version.\n\n{description}";
        }

        endpoint.WithName($"{endpointNamePrefix}_{operationId}");
        endpoint.WithSummary(summary);
        endpoint.WithDescription(description);
        endpoint.WithTags(tag);

        // Mark as deprecated if configured
        if (openApiConfig.Deprecated)
        {
#pragma warning disable ASPDEPR002
            endpoint.WithOpenApi(op =>
            {
                op.Deprecated = true;
                return op;
            });
#pragma warning restore ASPDEPR002
        }

        if (config.IsAnonymous(operation))
        {
            endpoint.AllowAnonymous();
        }
        else
        {
            var policies = config.GetPolicies(operation);
            if (policies is { Length: > 0 })
            {
                endpoint.RequireAuthorization(policies);
            }
            else
            {
                endpoint.RequireAuthorization();
            }
        }

        // Rate limiting — opt-in, applied after authorization
        if (config.IsRateLimitingDisabled(operation))
        {
            endpoint.DisableRateLimiting();
        }
        else
        {
            var rateLimitPolicy = config.GetRateLimitPolicy(operation);
            if (rateLimitPolicy is not null)
            {
                endpoint.RequireRateLimiting(rateLimitPolicy);
            }
        }
    }

    /// <summary>
    /// Configures the GetAll endpoint with proper OpenAPI documentation.
    /// </summary>
    internal static void ConfigureGetAllEndpoint<TEntity, TKey>(
        RouteHandlerBuilder endpoint,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        string entityName,
        string endpointNamePrefix)
        where TEntity : class
    {
        ConfigureEndpointBase(
            endpoint,
            RestLibOperation.GetAll,
            config,
            entityName,
            endpointNamePrefix,
            "GetAll",
            $"Get all {entityName} entities (paginated)",
            $"Returns a paginated list of {entityName} entities. " +
            "Supports cursor-based pagination via the `cursor` and `limit` query parameters. " +
            "Results are wrapped in a collection object with pagination links.");

#pragma warning disable ASPDEPR002
        endpoint.WithOpenApi(operation =>
        {
            // Document cursor parameter
            AddOrUpdateParameter(operation, "cursor", ParameterLocation.Query, false,
                "Opaque cursor for pagination. Use the value from the `next` link in the response to get the next page.",
                new OpenApiSchema { Type = JsonSchemaType.String });

            // Document limit parameter
            AddOrUpdateParameter(operation, "limit", ParameterLocation.Query, false,
                "Maximum number of items to return per page. Valid range: 1-100. Default: 20.",
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Minimum = "1",
                    Maximum = "100",
                    Default = JsonValue.Create(20)
                });

            // Document responses
            operation.Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = $"Successful retrieval of {entityName} list",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = CreateCollectionResponseSchema(entityName)
                        }
                    }
                },
                ["400"] = CreateProblemDetailsResponse("Bad Request - Invalid cursor or limit parameter"),
                ["401"] = CreateProblemDetailsResponse("Unauthorized - Authentication required"),
                ["403"] = CreateProblemDetailsResponse("Forbidden - Insufficient permissions")
            };

            return operation;
        });
#pragma warning restore ASPDEPR002
    }

    /// <summary>
    /// Configures the GetById endpoint with proper OpenAPI documentation.
    /// </summary>
    internal static void ConfigureGetByIdEndpoint<TEntity, TKey>(
        RouteHandlerBuilder endpoint,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        string entityName,
        string endpointNamePrefix)
        where TEntity : class
    {
        ConfigureEndpointBase(
            endpoint,
            RestLibOperation.GetById,
            config,
            entityName,
            endpointNamePrefix,
            "GetById",
            $"Get a {entityName} by ID",
            $"Returns a single {entityName} entity by its unique identifier. " +
            "Supports conditional requests via If-None-Match header when ETag support is enabled.");

#pragma warning disable ASPDEPR002
        endpoint.WithOpenApi(operation =>
        {
            // Document id parameter
            AddOrUpdateParameter(operation, "id", ParameterLocation.Path, true,
                $"The unique identifier of the {entityName}",
                GetOpenApiSchema(typeof(TKey)));

            // Document If-None-Match header
            AddOrUpdateParameter(operation, "If-None-Match", ParameterLocation.Header, false,
                "ETag value for conditional request. Returns 304 Not Modified if the resource hasn't changed.",
                new OpenApiSchema { Type = JsonSchemaType.String });

            // Document responses
            operation.Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = $"Successful retrieval of {entityName}",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = CreateEntityRefSchema(entityName)
                        }
                    },
                    Headers = new Dictionary<string, IOpenApiHeader>
                    {
                        ["ETag"] = new OpenApiHeader
                        {
                            Description = "Entity tag for cache validation",
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                        }
                    }
                },
                ["304"] = new OpenApiResponse { Description = "Not Modified - Resource hasn't changed since the ETag was generated" },
                ["401"] = CreateProblemDetailsResponse("Unauthorized - Authentication required"),
                ["403"] = CreateProblemDetailsResponse("Forbidden - Insufficient permissions"),
                ["404"] = CreateProblemDetailsResponse($"{entityName} not found")
            };

            return operation;
        });
#pragma warning restore ASPDEPR002
    }

    /// <summary>
    /// Configures the Create endpoint with proper OpenAPI documentation.
    /// </summary>
    internal static void ConfigureCreateEndpoint<TEntity, TKey>(
        RouteHandlerBuilder endpoint,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        string entityName,
        string endpointNamePrefix)
        where TEntity : class
    {
        ConfigureEndpointBase(
            endpoint,
            RestLibOperation.Create,
            config,
            entityName,
            endpointNamePrefix,
            "Create",
            $"Create a new {entityName}",
            $"Creates a new {entityName} entity. " +
            "Returns the created entity with its generated ID and a Location header pointing to the new resource.");

#pragma warning disable ASPDEPR002
        endpoint.WithOpenApi(operation =>
        {
            // Document request body
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = $"The {entityName} to create",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = CreateEntityRefSchema(entityName)
                    }
                }
            };

            // Document responses
            operation.Responses = new OpenApiResponses
            {
                ["201"] = new OpenApiResponse
                {
                    Description = $"{entityName} created successfully",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = CreateEntityRefSchema(entityName)
                        }
                    },
                    Headers = new Dictionary<string, IOpenApiHeader>
                    {
                        ["Location"] = new OpenApiHeader
                        {
                            Description = "URL of the newly created resource",
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri" }
                        },
                        ["ETag"] = new OpenApiHeader
                        {
                            Description = "Entity tag for cache validation (when ETag support is enabled)",
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                        }
                    }
                },
                ["400"] = CreateProblemDetailsResponse("Bad Request - Invalid request body"),
                ["401"] = CreateProblemDetailsResponse("Unauthorized - Authentication required"),
                ["403"] = CreateProblemDetailsResponse("Forbidden - Insufficient permissions")
            };

            return operation;
        });
#pragma warning restore ASPDEPR002
    }

    /// <summary>
    /// Configures the Update endpoint with proper OpenAPI documentation.
    /// </summary>
    internal static void ConfigureUpdateEndpoint<TEntity, TKey>(
        RouteHandlerBuilder endpoint,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        string entityName,
        string endpointNamePrefix)
        where TEntity : class
    {
        ConfigureEndpointBase(
            endpoint,
            RestLibOperation.Update,
            config,
            entityName,
            endpointNamePrefix,
            "Update",
            $"Fully update a {entityName}",
            $"Replaces an existing {entityName} entity with the provided data. " +
            "Supports optimistic locking via If-Match header when ETag support is enabled. " +
            "All fields must be provided as this performs a full replacement.");

#pragma warning disable ASPDEPR002
        endpoint.WithOpenApi(operation =>
        {
            // Document id parameter
            AddOrUpdateParameter(operation, "id", ParameterLocation.Path, true,
                $"The unique identifier of the {entityName} to update",
                GetOpenApiSchema(typeof(TKey)));

            // Document If-Match header
            AddOrUpdateParameter(operation, "If-Match", ParameterLocation.Header, false,
                "ETag value for optimistic locking. Update will fail with 412 if the resource has been modified.",
                new OpenApiSchema { Type = JsonSchemaType.String });

            // Document request body
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = $"The complete {entityName} data for the update",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = CreateEntityRefSchema(entityName)
                    }
                }
            };

            // Document responses
            operation.Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = $"{entityName} updated successfully",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = CreateEntityRefSchema(entityName)
                        }
                    },
                    Headers = new Dictionary<string, IOpenApiHeader>
                    {
                        ["ETag"] = new OpenApiHeader
                        {
                            Description = "New entity tag after update (when ETag support is enabled)",
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                        }
                    }
                },
                ["400"] = CreateProblemDetailsResponse("Bad Request - Invalid request body"),
                ["401"] = CreateProblemDetailsResponse("Unauthorized - Authentication required"),
                ["403"] = CreateProblemDetailsResponse("Forbidden - Insufficient permissions"),
                ["404"] = CreateProblemDetailsResponse($"{entityName} not found"),
                ["412"] = CreateProblemDetailsResponse("Precondition Failed - ETag mismatch (resource was modified)")
            };

            return operation;
        });
#pragma warning restore ASPDEPR002
    }

    /// <summary>
    /// Configures the Patch endpoint with proper OpenAPI documentation.
    /// </summary>
    internal static void ConfigurePatchEndpoint<TEntity, TKey>(
        RouteHandlerBuilder endpoint,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        string entityName,
        string endpointNamePrefix)
        where TEntity : class
    {
        ConfigureEndpointBase(
            endpoint,
            RestLibOperation.Patch,
            config,
            entityName,
            endpointNamePrefix,
            "Patch",
            $"Partially update a {entityName}",
            $"Partially updates an existing {entityName} entity using JSON Merge Patch (RFC 7396). " +
            "Only the provided fields will be updated. " +
            "Supports optimistic locking via If-Match header when ETag support is enabled.");

#pragma warning disable ASPDEPR002
        endpoint.WithOpenApi(operation =>
        {
            // Document id parameter
            AddOrUpdateParameter(operation, "id", ParameterLocation.Path, true,
                $"The unique identifier of the {entityName} to patch",
                GetOpenApiSchema(typeof(TKey)));

            // Document If-Match header
            AddOrUpdateParameter(operation, "If-Match", ParameterLocation.Header, false,
                "ETag value for optimistic locking. Patch will fail with 412 if the resource has been modified.",
                new OpenApiSchema { Type = JsonSchemaType.String });

            // Document request body
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = $"JSON Merge Patch document containing the fields to update",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Description = "Partial update document. Only include the fields you want to modify.",
                            AdditionalPropertiesAllowed = true
                        }
                    }
                }
            };

            // Document responses
            operation.Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = $"{entityName} patched successfully",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = CreateEntityRefSchema(entityName)
                        }
                    },
                    Headers = new Dictionary<string, IOpenApiHeader>
                    {
                        ["ETag"] = new OpenApiHeader
                        {
                            Description = "New entity tag after patch (when ETag support is enabled)",
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                        }
                    }
                },
                ["400"] = CreateProblemDetailsResponse("Bad Request - Invalid patch document"),
                ["401"] = CreateProblemDetailsResponse("Unauthorized - Authentication required"),
                ["403"] = CreateProblemDetailsResponse("Forbidden - Insufficient permissions"),
                ["404"] = CreateProblemDetailsResponse($"{entityName} not found"),
                ["412"] = CreateProblemDetailsResponse("Precondition Failed - ETag mismatch (resource was modified)")
            };

            return operation;
        });
#pragma warning restore ASPDEPR002
    }

    /// <summary>
    /// Configures the Delete endpoint with proper OpenAPI documentation.
    /// </summary>
    internal static void ConfigureDeleteEndpoint<TEntity, TKey>(
        RouteHandlerBuilder endpoint,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        string entityName,
        string endpointNamePrefix)
        where TEntity : class
    {
        ConfigureEndpointBase(
            endpoint,
            RestLibOperation.Delete,
            config,
            entityName,
            endpointNamePrefix,
            "Delete",
            $"Delete a {entityName}",
            $"Deletes an existing {entityName} entity. " +
            "Returns 204 No Content on success. " +
            "Supports optimistic locking via If-Match header when ETag support is enabled.");

#pragma warning disable ASPDEPR002
        endpoint.WithOpenApi(operation =>
        {
            // Document id parameter
            AddOrUpdateParameter(operation, "id", ParameterLocation.Path, true,
                $"The unique identifier of the {entityName} to delete",
                GetOpenApiSchema(typeof(TKey)));

            // Document If-Match header
            AddOrUpdateParameter(operation, "If-Match", ParameterLocation.Header, false,
                "ETag value for optimistic locking. Delete will fail with 412 if the resource has been modified.",
                new OpenApiSchema { Type = JsonSchemaType.String });

            // Document responses
            operation.Responses = new OpenApiResponses
            {
                ["204"] = new OpenApiResponse { Description = $"{entityName} deleted successfully" },
                ["401"] = CreateProblemDetailsResponse("Unauthorized - Authentication required"),
                ["403"] = CreateProblemDetailsResponse("Forbidden - Insufficient permissions"),
                ["404"] = CreateProblemDetailsResponse($"{entityName} not found"),
                ["412"] = CreateProblemDetailsResponse("Precondition Failed - ETag mismatch (resource was modified)")
            };

            return operation;
        });
#pragma warning restore ASPDEPR002
    }

    /// <summary>
    /// Configures the batch endpoint with OpenAPI metadata and conventions.
    /// </summary>
    internal static void ConfigureBatchEndpoint<TEntity, TKey>(
        RouteHandlerBuilder endpoint,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        string entityName,
        string endpointNamePrefix)
        where TEntity : class
    {
        // Use BatchCreate as the representative operation for auth/rate-limiting
        ConfigureEndpointBase(
            endpoint,
            RestLibOperation.BatchCreate,
            config,
            entityName,
            endpointNamePrefix,
            "Batch",
            $"Batch operations for {entityName}",
            $"Perform batch create, update, patch, or delete operations on {entityName} resources. " +
            $"Enabled actions: {string.Join(", ", config.EnabledBatchActions.Select(a => a.ToString().ToLowerInvariant()))}.");

#pragma warning disable ASPDEPR002
        endpoint.WithOpenApi(operation =>
        {
            // Document responses
            operation.Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse { Description = "All items processed successfully" },
                ["207"] = new OpenApiResponse { Description = "Multi-Status - some items succeeded, some failed" },
                ["400"] = CreateProblemDetailsResponse("Invalid batch request, size exceeded, or action not enabled"),
                ["401"] = CreateProblemDetailsResponse("Unauthorized - Authentication required"),
                ["403"] = CreateProblemDetailsResponse("Forbidden - Insufficient permissions")
            };

            return operation;
        });
#pragma warning restore ASPDEPR002
    }

    /// <summary>
    /// Gets the OpenAPI schema for a filter property type.
    /// </summary>
    internal static OpenApiSchema GetOpenApiSchema(Type propertyType)
    {
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        var isNullable = Nullable.GetUnderlyingType(propertyType) != null;

        var schema = new OpenApiSchema();

        if (underlyingType == typeof(string))
        {
            schema.Type = JsonSchemaType.String;
        }
        else if (underlyingType == typeof(bool))
        {
            schema.Type = JsonSchemaType.Boolean;
        }
        else if (underlyingType == typeof(int) || underlyingType == typeof(long) ||
                 underlyingType == typeof(short) || underlyingType == typeof(byte))
        {
            schema.Type = JsonSchemaType.Integer;
        }
        else if (underlyingType == typeof(decimal) || underlyingType == typeof(double) ||
                 underlyingType == typeof(float))
        {
            schema.Type = JsonSchemaType.Number;
        }
        else if (underlyingType == typeof(Guid))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "uuid";
        }
        else if (underlyingType == typeof(DateTime) || underlyingType == typeof(DateTimeOffset))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "date-time";
        }
        else if (underlyingType.IsEnum)
        {
            schema.Type = JsonSchemaType.String;
            schema.Enum = Enum.GetNames(underlyingType)
                .Select(name => (JsonNode)JsonValue.Create(name)!)
                .ToList();
        }
        else
        {
            schema.Type = JsonSchemaType.String;
        }

        // Mark as nullable if it's a nullable type by combining with JsonSchemaType.Null
        if (isNullable)
        {
            schema.Type |= JsonSchemaType.Null;
        }

        return schema;
    }

    /// <summary>
    /// Adds or updates a parameter in the OpenAPI operation.
    /// In OpenApi v2, parameter properties (Required, Schema) are read-only on
    /// <see cref="IOpenApiParameter"/>, so we must remove and re-add.
    /// </summary>
    private static void AddOrUpdateParameter(
        OpenApiOperation operation,
        string name,
        ParameterLocation location,
        bool required,
        string description,
        OpenApiSchema schema)
    {
        operation.Parameters ??= [];

        var existing = operation.Parameters.FirstOrDefault(p =>
            p.Name != null && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.In == location);

        if (existing is not null)
        {
            operation.Parameters.Remove(existing);
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = location,
            Required = required,
            Description = description,
            Schema = schema
        });
    }

    /// <summary>
    /// Creates an OpenAPI schema reference for an entity type.
    /// In OpenApi v2, schema references use <see cref="OpenApiSchemaReference"/> instead of
    /// <c>OpenApiSchema.Reference</c>.
    /// </summary>
    /// <param name="entityName">The schema name to reference.</param>
    /// <returns>An <see cref="OpenApiSchemaReference"/> pointing to the named schema.</returns>
    private static OpenApiSchemaReference CreateEntityRefSchema(string entityName)
    {
        return new OpenApiSchemaReference(entityName);
    }

    /// <summary>
    /// Creates a collection response schema for OpenAPI.
    /// </summary>
    private static OpenApiSchema CreateCollectionResponseSchema(string entityName)
    {
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["items"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = CreateEntityRefSchema(entityName)
                },
                ["self"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Format = "uri",
                    Description = "URL to the current page"
                },
                ["first"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Format = "uri",
                    Description = "URL to the first page"
                },
                ["next"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Format = "uri",
                    Description = "URL to the next page (null if on last page)"
                },
                ["prev"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Format = "uri",
                    Description = "URL to the previous page (null if on first page)"
                }
            },
            Required = new HashSet<string> { "items" }
        };
    }

    /// <summary>
    /// Creates a Problem Details response for OpenAPI.
    /// </summary>
    private static OpenApiResponse CreateProblemDetailsResponse(string description)
    {
        return new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["type"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Format = "uri",
                                Description = "A URI reference that identifies the problem type"
                            },
                            ["title"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Description = "A short, human-readable summary of the problem type"
                            },
                            ["status"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Integer,
                                Description = "The HTTP status code"
                            },
                            ["detail"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String | JsonSchemaType.Null,
                                Description = "A human-readable explanation specific to this occurrence"
                            },
                            ["instance"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String | JsonSchemaType.Null,
                                Format = "uri",
                                Description = "A URI reference that identifies the specific occurrence"
                            },
                            ["errors"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Object | JsonSchemaType.Null,
                                Description = "Validation errors keyed by field name",
                                AdditionalPropertiesAllowed = true,
                                AdditionalProperties = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Array,
                                    Items = new OpenApiSchema { Type = JsonSchemaType.String }
                                }
                            }
                        },
                        Required = new HashSet<string> { "type", "title", "status" }
                    }
                }
            }
        };
    }
}
