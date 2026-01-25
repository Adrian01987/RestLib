using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Configuration;
using RestLib.Filtering;
using RestLib.Hooks;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Serialization;

namespace RestLib;

/// <summary>
/// Extension methods for mapping RestLib CRUD endpoints.
/// </summary>
public static class RestLibEndpointExtensions
{
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
    var config = new RestLibEndpointConfiguration<TEntity, TKey>();
    configure?.Invoke(config);

    var group = endpoints.MapGroup(prefix);
    var entityName = typeof(TEntity).Name;

    // GET /prefix - Get all (paginated)
    var getAllEndpoint = group.MapGet("", async (
        IRepository<TEntity, TKey> repository,
        HttpContext httpContext,
        string? cursor,
        int? limit,
        CancellationToken ct) =>
    {
      var jsonOptions = GetJsonOptions(httpContext);
      var RestLibOptions = httpContext.RequestServices.GetService<RestLibOptions>()
                           ?? new RestLibOptions();

      // Create hook pipeline if hooks are configured
      var pipeline = config.Hooks is not null ? new HookPipeline<TEntity, TKey>(config.Hooks) : null;
      HookContext<TEntity, TKey>? hookContext = null;

      try
      {
        // OnRequestReceived hook
        if (pipeline is not null)
        {
          hookContext = pipeline.CreateContext(httpContext, RestLibOperation.GetAll);
          if (!await pipeline.ExecuteOnRequestReceivedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        // Validate cursor if provided
        if (!string.IsNullOrEmpty(cursor) && !CursorEncoder.IsValid(cursor))
        {
          return ProblemDetailsResult.InvalidCursor(
              cursor,
              httpContext.Request.Path,
              jsonOptions);
        }

        // Validate limit if provided
        if (limit.HasValue && (limit.Value < 1 || limit.Value > RestLibOptions.MaxPageSize))
        {
          return ProblemDetailsResult.InvalidLimit(
              limit.Value,
              1,
              RestLibOptions.MaxPageSize,
              httpContext.Request.Path,
              jsonOptions);
        }

        // Parse and validate filters
        IReadOnlyList<FilterValue> filterValues = [];
        if (config.HasFilters)
        {
          var filterResult = FilterParser.Parse(httpContext.Request.Query, config.FilterConfiguration);
          if (!filterResult.IsValid)
          {
            return ProblemDetailsResult.InvalidFilters(
                filterResult.Errors,
                httpContext.Request.Path,
                jsonOptions);
          }
          filterValues = filterResult.Values;
        }

        // OnRequestValidated hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteOnRequestValidatedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        var effectiveLimit = Math.Clamp(limit ?? RestLibOptions.DefaultPageSize, 1, RestLibOptions.MaxPageSize);
        var paginationRequest = new PaginationRequest
        {
          Cursor = cursor,
          Limit = effectiveLimit,
          Filters = filterValues
        };

        var result = await repository.GetAllAsync(paginationRequest, ct);

        var response = BuildCollectionResponse(result, httpContext.Request, cursor, effectiveLimit, RestLibOptions);

        // BeforeResponse hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteBeforeResponseAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        return Results.Json(response, jsonOptions);
      }
      catch (Exception ex)
      {
        if (pipeline is not null)
        {
          var errorContext = pipeline.CreateErrorContext(httpContext, RestLibOperation.GetAll, ex);
          var (handled, errorResult) = await pipeline.ExecuteOnErrorAsync(errorContext);
          if (handled && errorResult is not null)
          {
            return errorResult;
          }
        }
        throw;
      }
    });
    ConfigureGetAllEndpoint(getAllEndpoint, config, entityName);

    // Add OpenAPI documentation for filter parameters
    if (config.HasFilters)
    {
      getAllEndpoint.WithOpenApi(operation =>
      {
        foreach (var filter in config.FilterConfiguration.Properties)
        {
          var param = new OpenApiParameter
          {
            Name = filter.QueryParameterName,
            In = ParameterLocation.Query,
            Required = false,
            Description = $"Filter by {filter.PropertyName}",
            Schema = GetOpenApiSchema(filter.PropertyType)
          };
          operation.Parameters.Add(param);
        }
        return operation;
      });
    }

    // GET /prefix/{id} - Get by ID
    var getByIdEndpoint = group.MapGet("/{id}", async (
        TKey id,
        IRepository<TEntity, TKey> repository,
        HttpContext httpContext,
        CancellationToken ct) =>
    {
      var jsonOptions = GetJsonOptions(httpContext);
      var RestLibOptions = httpContext.RequestServices.GetService<RestLibOptions>()
                           ?? new RestLibOptions();

      // Create hook pipeline if hooks are configured
      var pipeline = config.Hooks is not null ? new HookPipeline<TEntity, TKey>(config.Hooks) : null;
      HookContext<TEntity, TKey>? hookContext = null;

      try
      {
        // OnRequestReceived hook
        if (pipeline is not null)
        {
          hookContext = pipeline.CreateContext(httpContext, RestLibOperation.GetById, id);
          if (!await pipeline.ExecuteOnRequestReceivedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        var entity = await repository.GetByIdAsync(id, ct);

        if (entity is null)
        {
          return ProblemDetailsResult.NotFound(
              entityName,
              id!,
              httpContext.Request.Path,
              jsonOptions);
        }

        // Update hook context with entity
        if (hookContext is not null)
        {
          hookContext.Entity = entity;
        }

        // OnRequestValidated hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteOnRequestValidatedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        // Handle conditional requests when ETag support is enabled
        if (RestLibOptions.EnableETagSupport)
        {
          var etagGenerator = httpContext.RequestServices.GetService<IETagGenerator>()
                              ?? new HashBasedETagGenerator(jsonOptions);
          var etag = etagGenerator.Generate(entity);

          // Check If-None-Match header for conditional GET
          var ifNoneMatch = httpContext.Request.Headers.IfNoneMatch;
          if (!ETagComparer.IfNoneMatchSucceeds(ifNoneMatch, etag))
          {
            // ETag matches - return 304 Not Modified
            httpContext.Response.Headers.ETag = etag;
            return Results.StatusCode(StatusCodes.Status304NotModified);
          }

          httpContext.Response.Headers.ETag = etag;
        }

        // BeforeResponse hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteBeforeResponseAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        return Results.Json(entity, jsonOptions);
      }
      catch (Exception ex)
      {
        if (pipeline is not null)
        {
          var errorContext = pipeline.CreateErrorContext(httpContext, RestLibOperation.GetById, ex, id);
          var (handled, errorResult) = await pipeline.ExecuteOnErrorAsync(errorContext);
          if (handled && errorResult is not null)
          {
            return errorResult;
          }
        }
        throw;
      }
    });
    ConfigureGetByIdEndpoint(getByIdEndpoint, config, entityName);

    // POST /prefix - Create
    var createEndpoint = group.MapPost("", async (
        TEntity entity,
        IRepository<TEntity, TKey> repository,
        HttpContext httpContext,
        CancellationToken ct) =>
    {
      var jsonOptions = GetJsonOptions(httpContext);
      var RestLibOptions = httpContext.RequestServices.GetService<RestLibOptions>()
                           ?? new RestLibOptions();

      // Create hook pipeline if hooks are configured
      var pipeline = config.Hooks is not null ? new HookPipeline<TEntity, TKey>(config.Hooks) : null;
      HookContext<TEntity, TKey>? hookContext = null;

      try
      {
        // OnRequestReceived hook
        if (pipeline is not null)
        {
          hookContext = pipeline.CreateContext(httpContext, RestLibOperation.Create, entity: entity);
          if (!await pipeline.ExecuteOnRequestReceivedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
          // Entity might have been modified by hook
          entity = hookContext.Entity ?? entity;
        }

        // Validate entity using Data Annotations
        if (RestLibOptions.EnableValidation)
        {
          var validationResult = Validation.EntityValidator.Validate(entity, RestLibOptions.JsonNamingPolicy);
          if (!validationResult.IsValid)
          {
            return ProblemDetailsResult.ValidationFailed(
                validationResult.Errors.ToDictionary(e => e.Key, e => e.Value),
                httpContext.Request.Path,
                jsonOptions);
          }
        }

        // OnRequestValidated hook
        if (pipeline is not null && hookContext is not null)
        {
          hookContext.Entity = entity;
          if (!await pipeline.ExecuteOnRequestValidatedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
          entity = hookContext.Entity ?? entity;
        }

        // BeforePersist hook
        if (pipeline is not null && hookContext is not null)
        {
          hookContext.Entity = entity;
          if (!await pipeline.ExecuteBeforePersistAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
          entity = hookContext.Entity ?? entity;
        }

        var created = await repository.CreateAsync(entity, ct);

        // AfterPersist hook
        if (pipeline is not null && hookContext is not null)
        {
          hookContext.Entity = created;
          if (!await pipeline.ExecuteAfterPersistAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        // Extract ID from created entity and set Location header
        var createdId = GetEntityKey(created, config.KeySelector);
        var location = $"{httpContext.Request.Path}/{createdId}";
        httpContext.Response.Headers.Location = location;

        // Add ETag header when enabled
        if (RestLibOptions.EnableETagSupport)
        {
          var etagGenerator = httpContext.RequestServices.GetService<IETagGenerator>()
                              ?? new HashBasedETagGenerator(jsonOptions);
          httpContext.Response.Headers.ETag = etagGenerator.Generate(created);
        }

        // BeforeResponse hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteBeforeResponseAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        return Results.Json(created, jsonOptions, statusCode: StatusCodes.Status201Created);
      }
      catch (Exception ex)
      {
        if (pipeline is not null)
        {
          var errorContext = pipeline.CreateErrorContext(httpContext, RestLibOperation.Create, ex, entity: entity);
          var (handled, errorResult) = await pipeline.ExecuteOnErrorAsync(errorContext);
          if (handled && errorResult is not null)
          {
            return errorResult;
          }
        }
        throw;
      }
    });
    ConfigureCreateEndpoint(createEndpoint, config, entityName);

    // PUT /prefix/{id} - Full Update
    var updateEndpoint = group.MapPut("/{id}", async (
        TKey id,
        TEntity entity,
        IRepository<TEntity, TKey> repository,
        HttpContext httpContext,
        CancellationToken ct) =>
    {
      var jsonOptions = GetJsonOptions(httpContext);
      var RestLibOptions = httpContext.RequestServices.GetService<RestLibOptions>()
                           ?? new RestLibOptions();

      // Create hook pipeline if hooks are configured
      var pipeline = config.Hooks is not null ? new HookPipeline<TEntity, TKey>(config.Hooks) : null;
      HookContext<TEntity, TKey>? hookContext = null;
      TEntity? originalEntity = null;

      try
      {
        // OnRequestReceived hook
        if (pipeline is not null)
        {
          hookContext = pipeline.CreateContext(httpContext, RestLibOperation.Update, id, entity);
          if (!await pipeline.ExecuteOnRequestReceivedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
          entity = hookContext.Entity ?? entity;
        }

        // Validate entity using Data Annotations
        if (RestLibOptions.EnableValidation)
        {
          var validationResult = Validation.EntityValidator.Validate(entity, RestLibOptions.JsonNamingPolicy);
          if (!validationResult.IsValid)
          {
            return ProblemDetailsResult.ValidationFailed(
                validationResult.Errors.ToDictionary(e => e.Key, e => e.Value),
                httpContext.Request.Path,
                jsonOptions);
          }
        }

        // OnRequestValidated hook
        if (pipeline is not null && hookContext is not null)
        {
          hookContext.Entity = entity;
          if (!await pipeline.ExecuteOnRequestValidatedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
          entity = hookContext.Entity ?? entity;
        }

        // Check for ETag precondition (If-Match header)
        if (RestLibOptions.EnableETagSupport)
        {
          var ifMatchHeader = httpContext.Request.Headers.IfMatch;
          if (!Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty(ifMatchHeader))
          {
            // Get current entity to compare ETags
            var current = await repository.GetByIdAsync(id, ct);
            if (current is null)
            {
              return ProblemDetailsResult.NotFound(
                  entityName,
                  id!,
                  httpContext.Request.Path,
                  jsonOptions);
            }

            var etagGenerator = httpContext.RequestServices.GetService<IETagGenerator>()
                                ?? new HashBasedETagGenerator(jsonOptions);
            var currentETag = etagGenerator.Generate(current);

            if (!ETagComparer.IfMatchSucceeds(ifMatchHeader, currentETag))
            {
              return ProblemDetailsResult.PreconditionFailed(
                  "The resource has been modified since you last retrieved it.",
                  httpContext.Request.Path,
                  jsonOptions);
            }

            originalEntity = current;
          }
        }

        // Fetch original entity if not already fetched (for hooks)
        if (originalEntity is null && pipeline is not null)
        {
          originalEntity = await repository.GetByIdAsync(id, ct);
        }

        // BeforePersist hook
        if (pipeline is not null && hookContext is not null)
        {
          // Create new context with original entity
          hookContext = new HookContext<TEntity, TKey>
          {
            HttpContext = httpContext,
            Operation = RestLibOperation.Update,
            ResourceId = id,
            Entity = entity,
            OriginalEntity = originalEntity,
            Services = httpContext.RequestServices,
            CancellationToken = ct
          };
          if (!await pipeline.ExecuteBeforePersistAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
          entity = hookContext.Entity ?? entity;
        }

        var updated = await repository.UpdateAsync(id, entity, ct);

        if (updated is null)
        {
          return ProblemDetailsResult.NotFound(
              entityName,
              id!,
              httpContext.Request.Path,
              jsonOptions);
        }

        // AfterPersist hook
        if (pipeline is not null && hookContext is not null)
        {
          hookContext.Entity = updated;
          if (!await pipeline.ExecuteAfterPersistAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        // Add ETag header when enabled
        if (RestLibOptions.EnableETagSupport)
        {
          var etagGenerator = httpContext.RequestServices.GetService<IETagGenerator>()
                              ?? new HashBasedETagGenerator(jsonOptions);
          httpContext.Response.Headers.ETag = etagGenerator.Generate(updated);
        }

        // BeforeResponse hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteBeforeResponseAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        return Results.Json(updated, jsonOptions);
      }
      catch (Exception ex)
      {
        if (pipeline is not null)
        {
          var errorContext = pipeline.CreateErrorContext(httpContext, RestLibOperation.Update, ex, id, entity);
          var (handled, errorResult) = await pipeline.ExecuteOnErrorAsync(errorContext);
          if (handled && errorResult is not null)
          {
            return errorResult;
          }
        }
        throw;
      }
    });
    ConfigureUpdateEndpoint(updateEndpoint, config, entityName);

    // PATCH /prefix/{id} - Partial Update (JSON Merge Patch - RFC 7396)
    var patchEndpoint = group.MapPatch("/{id}", async (
        TKey id,
        JsonElement patchDocument,
        IRepository<TEntity, TKey> repository,
        HttpContext httpContext,
        CancellationToken ct) =>
    {
      var jsonOptions = GetJsonOptions(httpContext);
      var RestLibOptions = httpContext.RequestServices.GetService<RestLibOptions>()
                           ?? new RestLibOptions();

      // Create hook pipeline if hooks are configured
      var pipeline = config.Hooks is not null ? new HookPipeline<TEntity, TKey>(config.Hooks) : null;
      HookContext<TEntity, TKey>? hookContext = null;
      TEntity? originalEntity = null;

      try
      {
        // OnRequestReceived hook
        if (pipeline is not null)
        {
          hookContext = pipeline.CreateContext(httpContext, RestLibOperation.Patch, id);
          if (!await pipeline.ExecuteOnRequestReceivedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        // OnRequestValidated hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteOnRequestValidatedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        // Check for ETag precondition (If-Match header)
        if (RestLibOptions.EnableETagSupport)
        {
          var ifMatchHeader = httpContext.Request.Headers.IfMatch;
          if (!Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty(ifMatchHeader))
          {
            // Get current entity to compare ETags
            var current = await repository.GetByIdAsync(id, ct);
            if (current is null)
            {
              return ProblemDetailsResult.NotFound(
                  entityName,
                  id!,
                  httpContext.Request.Path,
                  jsonOptions);
            }

            var etagGenerator = httpContext.RequestServices.GetService<IETagGenerator>()
                                ?? new HashBasedETagGenerator(jsonOptions);
            var currentETag = etagGenerator.Generate(current);

            if (!ETagComparer.IfMatchSucceeds(ifMatchHeader, currentETag))
            {
              return ProblemDetailsResult.PreconditionFailed(
                  "The resource has been modified since you last retrieved it.",
                  httpContext.Request.Path,
                  jsonOptions);
            }

            originalEntity = current;
          }
        }

        // Fetch original entity if not already fetched (for hooks)
        if (originalEntity is null && pipeline is not null)
        {
          originalEntity = await repository.GetByIdAsync(id, ct);
        }

        // BeforePersist hook
        if (pipeline is not null && hookContext is not null)
        {
          // Create new context with original entity
          hookContext = new HookContext<TEntity, TKey>
          {
            HttpContext = httpContext,
            Operation = RestLibOperation.Patch,
            ResourceId = id,
            Entity = originalEntity,
            OriginalEntity = originalEntity,
            Services = httpContext.RequestServices,
            CancellationToken = ct
          };
          if (!await pipeline.ExecuteBeforePersistAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        var patched = await repository.PatchAsync(id, patchDocument, ct);

        if (patched is null)
        {
          return ProblemDetailsResult.NotFound(
              entityName,
              id!,
              httpContext.Request.Path,
              jsonOptions);
        }

        // Validate patched entity using Data Annotations
        if (RestLibOptions.EnableValidation)
        {
          var validationResult = Validation.EntityValidator.Validate(patched, RestLibOptions.JsonNamingPolicy);
          if (!validationResult.IsValid)
          {
            return ProblemDetailsResult.ValidationFailed(
                validationResult.Errors.ToDictionary(e => e.Key, e => e.Value),
                httpContext.Request.Path,
                jsonOptions);
          }
        }

        // AfterPersist hook
        if (pipeline is not null && hookContext is not null)
        {
          hookContext.Entity = patched;
          if (!await pipeline.ExecuteAfterPersistAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        // Add ETag header when enabled
        if (RestLibOptions.EnableETagSupport)
        {
          var etagGenerator = httpContext.RequestServices.GetService<IETagGenerator>()
                              ?? new HashBasedETagGenerator(jsonOptions);
          httpContext.Response.Headers.ETag = etagGenerator.Generate(patched);
        }

        // BeforeResponse hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteBeforeResponseAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        return Results.Json(patched, jsonOptions);
      }
      catch (Exception ex)
      {
        if (pipeline is not null)
        {
          var errorContext = pipeline.CreateErrorContext(httpContext, RestLibOperation.Patch, ex, id, originalEntity);
          var (handled, errorResult) = await pipeline.ExecuteOnErrorAsync(errorContext);
          if (handled && errorResult is not null)
          {
            return errorResult;
          }
        }
        throw;
      }
    });
    ConfigurePatchEndpoint(patchEndpoint, config, entityName);

    // DELETE /prefix/{id} - Delete
    var deleteEndpoint = group.MapDelete("/{id}", async (
        TKey id,
        IRepository<TEntity, TKey> repository,
        HttpContext httpContext,
        CancellationToken ct) =>
    {
      var jsonOptions = GetJsonOptions(httpContext);

      // Create hook pipeline if hooks are configured
      var pipeline = config.Hooks is not null ? new HookPipeline<TEntity, TKey>(config.Hooks) : null;
      HookContext<TEntity, TKey>? hookContext = null;
      TEntity? entityToDelete = null;

      try
      {
        // OnRequestReceived hook
        if (pipeline is not null)
        {
          hookContext = pipeline.CreateContext(httpContext, RestLibOperation.Delete, id);
          if (!await pipeline.ExecuteOnRequestReceivedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        // OnRequestValidated hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteOnRequestValidatedAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        // Fetch entity for hooks if pipeline exists
        if (pipeline is not null)
        {
          entityToDelete = await repository.GetByIdAsync(id, ct);
        }

        // BeforePersist hook
        if (pipeline is not null && hookContext is not null)
        {
          hookContext.Entity = entityToDelete;
          if (!await pipeline.ExecuteBeforePersistAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        var deleted = await repository.DeleteAsync(id, ct);

        if (!deleted)
        {
          return ProblemDetailsResult.NotFound(
              entityName,
              id!,
              httpContext.Request.Path,
              jsonOptions);
        }

        // AfterPersist hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteAfterPersistAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        // BeforeResponse hook
        if (pipeline is not null && hookContext is not null)
        {
          if (!await pipeline.ExecuteBeforeResponseAsync(hookContext))
          {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
          }
        }

        return Results.NoContent();
      }
      catch (Exception ex)
      {
        if (pipeline is not null)
        {
          var errorContext = pipeline.CreateErrorContext(httpContext, RestLibOperation.Delete, ex, id, entityToDelete);
          var (handled, errorResult) = await pipeline.ExecuteOnErrorAsync(errorContext);
          if (handled && errorResult is not null)
          {
            return errorResult;
          }
        }
        throw;
      }
    });
    ConfigureDeleteEndpoint(deleteEndpoint, config, entityName);

    return group;
  }

  /// <summary>
  /// Base configuration applied to all endpoints.
  /// </summary>
  private static void ConfigureEndpointBase<TEntity, TKey>(
      RouteHandlerBuilder endpoint,
      RestLibOperation operation,
      RestLibEndpointConfiguration<TEntity, TKey> config,
      string entityName,
      string operationId,
      string defaultSummary,
      string defaultDescription)
      where TEntity : class
  {
    var openApiConfig = config.OpenApi;

    // Use custom tag or fall back to entity name
    var tag = openApiConfig.Tag ?? entityName;

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

    endpoint.WithName($"{entityName}_{operationId}");
    endpoint.WithSummary(summary);
    endpoint.WithDescription(description);
    endpoint.WithTags(tag);

    // Mark as deprecated if configured
    if (openApiConfig.Deprecated)
    {
      endpoint.WithOpenApi(operation =>
      {
        operation.Deprecated = true;
        return operation;
      });
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
  }

  /// <summary>
  /// Configures the GetAll endpoint with proper OpenAPI documentation.
  /// </summary>
  private static void ConfigureGetAllEndpoint<TEntity, TKey>(
      RouteHandlerBuilder endpoint,
      RestLibEndpointConfiguration<TEntity, TKey> config,
      string entityName)
      where TEntity : class
  {
    ConfigureEndpointBase(
        endpoint,
        RestLibOperation.GetAll,
        config,
        entityName,
        "GetAll",
        $"Get all {entityName} entities (paginated)",
        $"Returns a paginated list of {entityName} entities. " +
        "Supports cursor-based pagination via the `cursor` and `limit` query parameters. " +
        "Results are wrapped in a collection object with pagination links.");

    endpoint.WithOpenApi(operation =>
    {
      // Document cursor parameter
      AddOrUpdateParameter(operation, "cursor", ParameterLocation.Query, false,
          "Opaque cursor for pagination. Use the value from the `next` link in the response to get the next page.",
          new OpenApiSchema { Type = "string" });

      // Document limit parameter
      AddOrUpdateParameter(operation, "limit", ParameterLocation.Query, false,
          "Maximum number of items to return per page. Valid range: 1-100. Default: 20.",
          new OpenApiSchema
          {
            Type = "integer",
            Format = "int32",
            Minimum = 1,
            Maximum = 100,
            Default = new Microsoft.OpenApi.Any.OpenApiInteger(20)
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
  }

  /// <summary>
  /// Configures the GetById endpoint with proper OpenAPI documentation.
  /// </summary>
  private static void ConfigureGetByIdEndpoint<TEntity, TKey>(
      RouteHandlerBuilder endpoint,
      RestLibEndpointConfiguration<TEntity, TKey> config,
      string entityName)
      where TEntity : class
  {
    ConfigureEndpointBase(
        endpoint,
        RestLibOperation.GetById,
        config,
        entityName,
        "GetById",
        $"Get a {entityName} by ID",
        $"Returns a single {entityName} entity by its unique identifier. " +
        "Supports conditional requests via If-None-Match header when ETag support is enabled.");

    endpoint.WithOpenApi(operation =>
    {
      // Document id parameter
      AddOrUpdateParameter(operation, "id", ParameterLocation.Path, true,
          $"The unique identifier of the {entityName}",
          GetOpenApiSchema(typeof(TKey)));

      // Document If-None-Match header
      AddOrUpdateParameter(operation, "If-None-Match", ParameterLocation.Header, false,
          "ETag value for conditional request. Returns 304 Not Modified if the resource hasn't changed.",
          new OpenApiSchema { Type = "string" });

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
              Schema = new OpenApiSchema
              {
                Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = entityName }
              }
            }
          },
          Headers = new Dictionary<string, OpenApiHeader>
          {
            ["ETag"] = new OpenApiHeader
            {
              Description = "Entity tag for cache validation",
              Schema = new OpenApiSchema { Type = "string" }
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
  }

  /// <summary>
  /// Configures the Create endpoint with proper OpenAPI documentation.
  /// </summary>
  private static void ConfigureCreateEndpoint<TEntity, TKey>(
      RouteHandlerBuilder endpoint,
      RestLibEndpointConfiguration<TEntity, TKey> config,
      string entityName)
      where TEntity : class
  {
    ConfigureEndpointBase(
        endpoint,
        RestLibOperation.Create,
        config,
        entityName,
        "Create",
        $"Create a new {entityName}",
        $"Creates a new {entityName} entity. " +
        "Returns the created entity with its generated ID and a Location header pointing to the new resource.");

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
            Schema = new OpenApiSchema
            {
              Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = entityName }
            }
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
              Schema = new OpenApiSchema
              {
                Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = entityName }
              }
            }
          },
          Headers = new Dictionary<string, OpenApiHeader>
          {
            ["Location"] = new OpenApiHeader
            {
              Description = "URL of the newly created resource",
              Schema = new OpenApiSchema { Type = "string", Format = "uri" }
            },
            ["ETag"] = new OpenApiHeader
            {
              Description = "Entity tag for cache validation (when ETag support is enabled)",
              Schema = new OpenApiSchema { Type = "string" }
            }
          }
        },
        ["400"] = CreateProblemDetailsResponse("Bad Request - Invalid request body"),
        ["401"] = CreateProblemDetailsResponse("Unauthorized - Authentication required"),
        ["403"] = CreateProblemDetailsResponse("Forbidden - Insufficient permissions")
      };

      return operation;
    });
  }

  /// <summary>
  /// Configures the Update endpoint with proper OpenAPI documentation.
  /// </summary>
  private static void ConfigureUpdateEndpoint<TEntity, TKey>(
      RouteHandlerBuilder endpoint,
      RestLibEndpointConfiguration<TEntity, TKey> config,
      string entityName)
      where TEntity : class
  {
    ConfigureEndpointBase(
        endpoint,
        RestLibOperation.Update,
        config,
        entityName,
        "Update",
        $"Fully update a {entityName}",
        $"Replaces an existing {entityName} entity with the provided data. " +
        "Supports optimistic locking via If-Match header when ETag support is enabled. " +
        "All fields must be provided as this performs a full replacement.");

    endpoint.WithOpenApi(operation =>
    {
      // Document id parameter
      AddOrUpdateParameter(operation, "id", ParameterLocation.Path, true,
          $"The unique identifier of the {entityName} to update",
          GetOpenApiSchema(typeof(TKey)));

      // Document If-Match header
      AddOrUpdateParameter(operation, "If-Match", ParameterLocation.Header, false,
          "ETag value for optimistic locking. Update will fail with 412 if the resource has been modified.",
          new OpenApiSchema { Type = "string" });

      // Document request body
      operation.RequestBody = new OpenApiRequestBody
      {
        Required = true,
        Description = $"The complete {entityName} data for the update",
        Content = new Dictionary<string, OpenApiMediaType>
        {
          ["application/json"] = new OpenApiMediaType
          {
            Schema = new OpenApiSchema
            {
              Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = entityName }
            }
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
              Schema = new OpenApiSchema
              {
                Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = entityName }
              }
            }
          },
          Headers = new Dictionary<string, OpenApiHeader>
          {
            ["ETag"] = new OpenApiHeader
            {
              Description = "New entity tag after update (when ETag support is enabled)",
              Schema = new OpenApiSchema { Type = "string" }
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
  }

  /// <summary>
  /// Configures the Patch endpoint with proper OpenAPI documentation.
  /// </summary>
  private static void ConfigurePatchEndpoint<TEntity, TKey>(
      RouteHandlerBuilder endpoint,
      RestLibEndpointConfiguration<TEntity, TKey> config,
      string entityName)
      where TEntity : class
  {
    ConfigureEndpointBase(
        endpoint,
        RestLibOperation.Patch,
        config,
        entityName,
        "Patch",
        $"Partially update a {entityName}",
        $"Partially updates an existing {entityName} entity using JSON Merge Patch (RFC 7396). " +
        "Only the provided fields will be updated. " +
        "Supports optimistic locking via If-Match header when ETag support is enabled.");

    endpoint.WithOpenApi(operation =>
    {
      // Document id parameter
      AddOrUpdateParameter(operation, "id", ParameterLocation.Path, true,
          $"The unique identifier of the {entityName} to patch",
          GetOpenApiSchema(typeof(TKey)));

      // Document If-Match header
      AddOrUpdateParameter(operation, "If-Match", ParameterLocation.Header, false,
          "ETag value for optimistic locking. Patch will fail with 412 if the resource has been modified.",
          new OpenApiSchema { Type = "string" });

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
              Type = "object",
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
              Schema = new OpenApiSchema
              {
                Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = entityName }
              }
            }
          },
          Headers = new Dictionary<string, OpenApiHeader>
          {
            ["ETag"] = new OpenApiHeader
            {
              Description = "New entity tag after patch (when ETag support is enabled)",
              Schema = new OpenApiSchema { Type = "string" }
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
  }

  /// <summary>
  /// Configures the Delete endpoint with proper OpenAPI documentation.
  /// </summary>
  private static void ConfigureDeleteEndpoint<TEntity, TKey>(
      RouteHandlerBuilder endpoint,
      RestLibEndpointConfiguration<TEntity, TKey> config,
      string entityName)
      where TEntity : class
  {
    ConfigureEndpointBase(
        endpoint,
        RestLibOperation.Delete,
        config,
        entityName,
        "Delete",
        $"Delete a {entityName}",
        $"Deletes an existing {entityName} entity. " +
        "Returns 204 No Content on success. " +
        "Supports optimistic locking via If-Match header when ETag support is enabled.");

    endpoint.WithOpenApi(operation =>
    {
      // Document id parameter
      AddOrUpdateParameter(operation, "id", ParameterLocation.Path, true,
          $"The unique identifier of the {entityName} to delete",
          GetOpenApiSchema(typeof(TKey)));

      // Document If-Match header
      AddOrUpdateParameter(operation, "If-Match", ParameterLocation.Header, false,
          "ETag value for optimistic locking. Delete will fail with 412 if the resource has been modified.",
          new OpenApiSchema { Type = "string" });

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
  }

  /// <summary>
  /// Adds or updates a parameter in the OpenAPI operation.
  /// </summary>
  private static void AddOrUpdateParameter(
      OpenApiOperation operation,
      string name,
      ParameterLocation location,
      bool required,
      string description,
      OpenApiSchema schema)
  {
    var existing = operation.Parameters.FirstOrDefault(p =>
        p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.In == location);

    if (existing is not null)
    {
      existing.Description = description;
      existing.Required = required;
      existing.Schema = schema;
    }
    else
    {
      operation.Parameters.Add(new OpenApiParameter
      {
        Name = name,
        In = location,
        Required = required,
        Description = description,
        Schema = schema
      });
    }
  }

  /// <summary>
  /// Creates a collection response schema for OpenAPI.
  /// </summary>
  private static OpenApiSchema CreateCollectionResponseSchema(string entityName)
  {
    return new OpenApiSchema
    {
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["items"] = new OpenApiSchema
        {
          Type = "array",
          Items = new OpenApiSchema
          {
            Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = entityName }
          }
        },
        ["self"] = new OpenApiSchema
        {
          Type = "string",
          Format = "uri",
          Description = "URL to the current page",
          Nullable = true
        },
        ["first"] = new OpenApiSchema
        {
          Type = "string",
          Format = "uri",
          Description = "URL to the first page",
          Nullable = true
        },
        ["next"] = new OpenApiSchema
        {
          Type = "string",
          Format = "uri",
          Description = "URL to the next page (null if on last page)",
          Nullable = true
        },
        ["prev"] = new OpenApiSchema
        {
          Type = "string",
          Format = "uri",
          Description = "URL to the previous page (null if on first page)",
          Nullable = true
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
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
              ["type"] = new OpenApiSchema
              {
                Type = "string",
                Format = "uri",
                Description = "A URI reference that identifies the problem type"
              },
              ["title"] = new OpenApiSchema
              {
                Type = "string",
                Description = "A short, human-readable summary of the problem type"
              },
              ["status"] = new OpenApiSchema
              {
                Type = "integer",
                Description = "The HTTP status code"
              },
              ["detail"] = new OpenApiSchema
              {
                Type = "string",
                Description = "A human-readable explanation specific to this occurrence",
                Nullable = true
              },
              ["instance"] = new OpenApiSchema
              {
                Type = "string",
                Format = "uri",
                Description = "A URI reference that identifies the specific occurrence",
                Nullable = true
              },
              ["errors"] = new OpenApiSchema
              {
                Type = "object",
                Description = "Validation errors keyed by field name",
                Nullable = true,
                AdditionalPropertiesAllowed = true,
                AdditionalProperties = new OpenApiSchema
                {
                  Type = "array",
                  Items = new OpenApiSchema { Type = "string" }
                }
              }
            },
            Required = new HashSet<string> { "type", "title", "status" }
          }
        }
      }
    };
  }

  /// <summary>
  /// Gets the JSON serializer options from the service provider,
  /// falling back to RestLib defaults if not registered.
  /// </summary>
  private static JsonSerializerOptions GetJsonOptions(HttpContext httpContext)
  {
    return httpContext.RequestServices.GetService<JsonSerializerOptions>()
           ?? RestLibJsonOptions.CreateDefault();
  }

  /// <summary>
  /// Builds a standardized collection response with pagination links.
  /// Links are fully-qualified URLs and preserve any query filters.
  /// </summary>
  private static CollectionResponse<T> BuildCollectionResponse<T>(
      PagedResult<T> result,
      HttpRequest request,
      string? currentCursor,
      int limit,
      RestLibOptions options)
  {
    string? selfLink = null;
    string? firstLink = null;
    string? nextLink = null;
    string? prevLink = null;

    if (options.IncludePaginationLinks)
    {
      var baseUrl = $"{request.Scheme}://{request.Host}{request.Path}";

      // Extract query filters (all query params except cursor and limit)
      var filterParams = GetFilterQueryParams(request.Query);

      // Build self link (includes cursor if present, always includes limit and filters)
      selfLink = BuildPaginationUrl(baseUrl, currentCursor, limit, filterParams);

      // Build first link (no cursor, includes limit and filters)
      firstLink = BuildPaginationUrl(baseUrl, null, limit, filterParams);

      // Build next link if there are more items
      if (result.NextCursor is not null)
      {
        nextLink = BuildPaginationUrl(baseUrl, result.NextCursor, limit, filterParams);
      }

      // Note: prev link requires tracking the previous cursor, which we don't have in simple cursor pagination
      // For now, prev is null (could be enhanced with bidirectional cursors later)
    }

    return new CollectionResponse<T>
    {
      Items = result.Items,
      Self = selfLink,
      First = firstLink,
      Next = nextLink,
      Prev = prevLink
    };
  }

  /// <summary>
  /// Extracts filter query parameters (all params except cursor and limit).
  /// </summary>
  private static IReadOnlyList<KeyValuePair<string, string>> GetFilterQueryParams(IQueryCollection query)
  {
    var filters = new List<KeyValuePair<string, string>>();
    foreach (var param in query)
    {
      // Skip pagination params - preserve all other filters
      if (string.Equals(param.Key, "cursor", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(param.Key, "limit", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      foreach (var value in param.Value)
      {
        if (value is not null)
        {
          filters.Add(new KeyValuePair<string, string>(param.Key, value));
        }
      }
    }
    return filters;
  }

  /// <summary>
  /// Builds a pagination URL with cursor, limit, and filter parameters.
  /// </summary>
  private static string BuildPaginationUrl(
      string baseUrl,
      string? cursor,
      int limit,
      IReadOnlyList<KeyValuePair<string, string>> filterParams)
  {
    var queryParams = new List<string>();

    // Add cursor if present
    if (!string.IsNullOrEmpty(cursor))
    {
      queryParams.Add($"cursor={Uri.EscapeDataString(cursor)}");
    }

    // Always add limit
    queryParams.Add($"limit={limit}");

    // Add all filter params (preserved from original request)
    foreach (var filter in filterParams)
    {
      queryParams.Add($"{Uri.EscapeDataString(filter.Key)}={Uri.EscapeDataString(filter.Value)}");
    }

    return queryParams.Count > 0 ? $"{baseUrl}?{string.Join("&", queryParams)}" : baseUrl;
  }

  /// <summary>
  /// Extracts the key from an entity using the configured key selector or reflection.
  /// </summary>
  private static TKey? GetEntityKey<TEntity, TKey>(TEntity entity, Func<TEntity, TKey>? keySelector)
      where TEntity : class
  {
    if (keySelector is not null)
    {
      return keySelector(entity);
    }

    // Fall back to reflection: look for 'Id' property
    var idProperty = typeof(TEntity).GetProperty("Id");
    if (idProperty is not null && idProperty.PropertyType == typeof(TKey))
    {
      return (TKey?)idProperty.GetValue(entity);
    }

    return default;
  }

  /// <summary>
  /// Gets the OpenAPI schema for a filter property type.
  /// </summary>
  private static Microsoft.OpenApi.Models.OpenApiSchema GetOpenApiSchema(Type propertyType)
  {
    var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

    var schema = new Microsoft.OpenApi.Models.OpenApiSchema();

    if (underlyingType == typeof(string))
    {
      schema.Type = "string";
    }
    else if (underlyingType == typeof(bool))
    {
      schema.Type = "boolean";
    }
    else if (underlyingType == typeof(int) || underlyingType == typeof(long) ||
             underlyingType == typeof(short) || underlyingType == typeof(byte))
    {
      schema.Type = "integer";
    }
    else if (underlyingType == typeof(decimal) || underlyingType == typeof(double) ||
             underlyingType == typeof(float))
    {
      schema.Type = "number";
    }
    else if (underlyingType == typeof(Guid))
    {
      schema.Type = "string";
      schema.Format = "uuid";
    }
    else if (underlyingType == typeof(DateTime) || underlyingType == typeof(DateTimeOffset))
    {
      schema.Type = "string";
      schema.Format = "date-time";
    }
    else if (underlyingType.IsEnum)
    {
      schema.Type = "string";
      schema.Enum = Enum.GetNames(underlyingType)
          .Select(name => (Microsoft.OpenApi.Any.IOpenApiAny)new Microsoft.OpenApi.Any.OpenApiString(name))
          .ToList();
    }
    else
    {
      schema.Type = "string";
    }

    // Mark as nullable if it's a nullable type
    if (Nullable.GetUnderlyingType(propertyType) != null)
    {
      schema.Nullable = true;
    }

    return schema;
  }
}
