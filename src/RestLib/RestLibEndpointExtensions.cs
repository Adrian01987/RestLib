using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using RestLib.Configuration;
using RestLib.Endpoints;
using RestLib.Filtering;

namespace RestLib;

/// <summary>
/// Extension methods for mapping RestLib CRUD endpoints.
/// </summary>
public static class RestLibEndpointExtensions
{
  /// <summary>
  /// Fallback registry used when <see cref="RestLibServiceExtensions.AddRestLib"/> has not been called.
  /// Prefer the DI-registered instance for proper test isolation.
  /// </summary>
  private static readonly EndpointNameRegistry _fallbackNameRegistry = new();

  /// <summary>
  /// Maps CRUD endpoints for the specified entity type.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <typeparam name="TKey">The key type.</typeparam>
  /// <param name="endpoints">The endpoint route builder.</param>
  /// <param name="prefix">The route prefix (e.g., "/api/products").</param>
  /// <param name="configure">Optional configuration action.</param>
  /// <returns>The route group builder for further customization.</returns>
  public static RouteGroupBuilder MapRestLib<TEntity, TKey>(
      this IEndpointRouteBuilder endpoints,
      string prefix,
      Action<RestLibEndpointConfiguration<TEntity, TKey>>? configure = null)
      where TEntity : class
  {
    var group = endpoints.MapGroup(prefix);
    ConfigureRestLibEndpoints(group, configure, prefix);
    return group;
  }

  /// <summary>
  /// Maps RestLib CRUD endpoints directly onto an existing route group.
  /// Use this overload when the group already has its route prefix configured
  /// (e.g., inside a versioned API group).
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <typeparam name="TKey">The key type.</typeparam>
  /// <param name="group">The route group builder to attach endpoints to.</param>
  /// <param name="configure">Optional configuration action.</param>
  /// <returns>The route group builder for further customization.</returns>
  public static RouteGroupBuilder MapRestLib<TEntity, TKey>(
      this RouteGroupBuilder group,
      Action<RestLibEndpointConfiguration<TEntity, TKey>>? configure = null)
      where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(group);
    ConfigureRestLibEndpoints(group, configure, routePrefix: null);
    return group;
  }

  /// <summary>
  /// Shared implementation that registers all CRUD endpoints on the provided group.
  /// </summary>
  private static void ConfigureRestLibEndpoints<TEntity, TKey>(
      RouteGroupBuilder group,
      Action<RestLibEndpointConfiguration<TEntity, TKey>>? configure,
      string? routePrefix)
      where TEntity : class
  {
    var config = new RestLibEndpointConfiguration<TEntity, TKey>();
    configure?.Invoke(config);

    var baseEntityName = typeof(TEntity).Name;

    // Generate a unique endpoint name prefix for OpenAPI operation IDs.
    // Build a candidate name from the entity type name and (optionally) the sanitized route prefix.
    // When the same candidate appears more than once (e.g., the same entity at the same
    // sub-prefix inside different versioned groups), append a numeric suffix to prevent
    // duplicate endpoint name collisions.
    string candidateName;
    if (!string.IsNullOrEmpty(routePrefix))
    {
      var sanitizedPrefix = SanitizeRoutePrefix(routePrefix);
      candidateName = $"{baseEntityName}_{sanitizedPrefix}";
    }
    else
    {
      candidateName = baseEntityName;
    }

    var nameRegistry = ((IEndpointRouteBuilder)group).ServiceProvider.GetService<EndpointNameRegistry>()
                       ?? _fallbackNameRegistry;
    var entityName = nameRegistry.GetUniqueEndpointName(candidateName);

    // Register tag description for the OpenAPI document if configured
    var openApiConfig = config.OpenApi;
    if (!string.IsNullOrEmpty(openApiConfig.TagDescription))
    {
      var tag = string.IsNullOrEmpty(openApiConfig.Tag) ? baseEntityName : openApiConfig.Tag;
      var tagRegistry = ((IEndpointRouteBuilder)group).ServiceProvider.GetService<TagDescriptionRegistry>();
      tagRegistry?.Set(tag, openApiConfig.TagDescription);
    }

    // GET /prefix - Get all (paginated)
    if (config.IsOperationEnabled(RestLibOperation.GetAll))
    {
      var getAllEndpoint = group.MapGet("", GetAllHandler.CreateDelegate<TEntity, TKey>(config));
      OpenApiEndpointConfiguration.ConfigureGetAllEndpoint(getAllEndpoint, config, baseEntityName, entityName);

      // Add OpenAPI documentation for filter parameters
      if (config.HasFilters)
      {
        getAllEndpoint.AddOpenApiOperationTransformer((operation, context, ct) =>
        {
          foreach (var filter in config.FilterConfiguration.Properties)
          {
            var operatorNames = filter.AllowedOperators
                .Select(FilterParser.GetOperatorName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            var description = filter.AllowedOperators.Count == 1
                ? $"Filter by {filter.PropertyName} (equality only). Example: ?{filter.QueryParameterName}=value"
                : $"Filter by {filter.PropertyName}. Allowed operators: {string.Join(", ", operatorNames)}. " +
                  $"Use bracket syntax for non-equality operators: ?{filter.QueryParameterName}[operator]=value";

            var param = new OpenApiParameter
            {
              Name = filter.QueryParameterName,
              In = ParameterLocation.Query,
              Required = false,
              Description = description,
              Schema = OpenApiEndpointConfiguration.GetOpenApiSchema(filter.PropertyType)
            };
            operation.Parameters ??= [];
            operation.Parameters.Add(param);
          }

          return Task.CompletedTask;
        });
      }

      // Add OpenAPI documentation for sort parameter
      if (config.HasSorting)
      {
        getAllEndpoint.AddOpenApiOperationTransformer((operation, context, ct) =>
        {
          var allowedFields = string.Join(", ",
              config.SortConfiguration.Properties.Select(p => p.QueryParameterName));

          operation.Parameters ??= [];
          operation.Parameters.Add(new OpenApiParameter
          {
            Name = "sort",
            In = ParameterLocation.Query,
            Required = false,
            Description = $"Sort by comma-separated property:direction pairs. " +
                          $"Allowed fields: {allowedFields}. Directions: asc, desc. " +
                          $"Example: price:asc,name:desc",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
          });

          return Task.CompletedTask;
        });
      }

      // Add OpenAPI documentation for fields parameter
      if (config.HasFieldSelection)
      {
        getAllEndpoint.AddOpenApiOperationTransformer((operation, context, ct) =>
        {
          var allowedFields = string.Join(", ",
              config.FieldSelectionConfiguration.Properties.Select(p => p.QueryFieldName));

          operation.Parameters ??= [];
          operation.Parameters.Add(new OpenApiParameter
          {
            Name = "fields",
            In = ParameterLocation.Query,
            Required = false,
            Description = $"Comma-separated list of fields to include in the response. " +
                          $"Allowed fields: {allowedFields}.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
          });

          return Task.CompletedTask;
        });
      }
    } // end GetAll

    // GET /prefix/{id} - Get by ID
    if (config.IsOperationEnabled(RestLibOperation.GetById))
    {
      var getByIdEndpoint = group.MapGet("/{id}", GetByIdHandler.CreateDelegate<TEntity, TKey>(config, entityName));
      OpenApiEndpointConfiguration.ConfigureGetByIdEndpoint(getByIdEndpoint, config, baseEntityName, entityName);

      // Add OpenAPI documentation for fields parameter
      if (config.HasFieldSelection)
      {
        getByIdEndpoint.AddOpenApiOperationTransformer((operation, context, ct) =>
        {
          var allowedFields = string.Join(", ",
              config.FieldSelectionConfiguration.Properties.Select(p => p.QueryFieldName));

          operation.Parameters ??= [];
          operation.Parameters.Add(new OpenApiParameter
          {
            Name = "fields",
            In = ParameterLocation.Query,
            Required = false,
            Description = $"Comma-separated list of fields to include in the response. " +
                          $"Allowed fields: {allowedFields}.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
          });

          return Task.CompletedTask;
        });
      }
    } // end GetById

    // POST /prefix - Create
    if (config.IsOperationEnabled(RestLibOperation.Create))
    {
      var createEndpoint = group.MapPost("", CreateHandler.CreateDelegate<TEntity, TKey>(config));
      OpenApiEndpointConfiguration.ConfigureCreateEndpoint(createEndpoint, config, baseEntityName, entityName);
    } // end Create

    // PUT /prefix/{id} - Full Update
    if (config.IsOperationEnabled(RestLibOperation.Update))
    {
      var updateEndpoint = group.MapPut("/{id}", UpdateHandler.CreateDelegate<TEntity, TKey>(config, entityName));
      OpenApiEndpointConfiguration.ConfigureUpdateEndpoint(updateEndpoint, config, baseEntityName, entityName);
    } // end Update

    // PATCH /prefix/{id} - Partial Update (JSON Merge Patch - RFC 7396)
    if (config.IsOperationEnabled(RestLibOperation.Patch))
    {
      var patchEndpoint = group.MapPatch("/{id}", PatchHandler.CreateDelegate<TEntity, TKey>(config, entityName));
      OpenApiEndpointConfiguration.ConfigurePatchEndpoint(patchEndpoint, config, baseEntityName, entityName);
    } // end Patch

    // DELETE /prefix/{id} - Delete
    if (config.IsOperationEnabled(RestLibOperation.Delete))
    {
      var deleteEndpoint = group.MapDelete("/{id}", DeleteHandler.CreateDelegate<TEntity, TKey>(config, entityName));
      OpenApiEndpointConfiguration.ConfigureDeleteEndpoint(deleteEndpoint, config, baseEntityName, entityName);
    } // end Delete

    // POST /prefix/batch - Batch operations
    if (config.HasBatch)
    {
      var batchEndpoint = group.MapPost("batch", BatchHandler.CreateDelegate<TEntity, TKey>(config));
      OpenApiEndpointConfiguration.ConfigureBatchEndpoint(batchEndpoint, config, baseEntityName, entityName);
    } // end Batch
  }

  /// <summary>
  /// Converts a route prefix into a valid identifier fragment for use in endpoint names.
  /// Strips leading/trailing slashes and replaces non-alphanumeric characters with underscores.
  /// </summary>
  private static string SanitizeRoutePrefix(string prefix)
  {
    var trimmed = prefix.Trim('/');
    var sb = new System.Text.StringBuilder(trimmed.Length);
    foreach (var ch in trimmed)
    {
      sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
    }

    return sb.ToString();
  }
}
