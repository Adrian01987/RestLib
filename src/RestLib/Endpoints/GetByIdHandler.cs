using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Configuration;
using RestLib.FieldSelection;
using RestLib.Hooks;

namespace RestLib.Endpoints;

/// <summary>
/// Handles GET requests for a single entity by ID.
/// </summary>
internal static class GetByIdHandler
{
    /// <summary>
    /// Creates the delegate for the GetById endpoint.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="entityName">The clean entity type name used in error messages (e.g., "Product").</param>
    /// <returns>The request delegate.</returns>
    internal static Func<TKey, IRepository<TEntity, TKey>, HttpContext, CancellationToken, Task<IResult>>
        CreateDelegate<TEntity, TKey>(
            RestLibEndpointConfiguration<TEntity, TKey> config,
            string entityName)
        where TEntity : class
    {
        return async (
            TKey id,
            IRepository<TEntity, TKey> repository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var (jsonOptions, options) = EndpointHelpers.ResolveOptions(httpContext);

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await EndpointHelpers.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.GetById, id);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;

            try
            {
                // Parse and validate field selection before hitting the database
                IReadOnlyList<SelectedField> selectedFields = [];
                if (config.HasFieldSelection)
                {
                    var rawFields = httpContext.Request.Query["fields"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(rawFields))
                    {
                        var fieldsResult = FieldSelectionParser.Parse(rawFields, config.FieldSelectionConfiguration);
                        if (!fieldsResult.IsValid)
                        {
                            return Responses.ProblemDetailsResult.InvalidFields(
                                fieldsResult.Errors,
                                httpContext.Request.Path,
                                jsonOptions);
                        }

                        selectedFields = fieldsResult.Fields;
                    }
                }

                var entity = await repository.GetByIdAsync(id, ct);

                if (entity is null)
                {
                    return Responses.ProblemDetailsResult.NotFound(
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
                var onValidatedResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
                if (onValidatedResult is not null) return onValidatedResult;

                // Handle conditional requests when ETag support is enabled
                if (options.EnableETagSupport)
                {
                    var etagGenerator = EndpointHelpers.ResolveETagGenerator(httpContext, jsonOptions);
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
                var beforeResponseResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseResult is not null) return beforeResponseResult;

                // Apply field selection projection if requested
                if (selectedFields.Count > 0)
                {
                    var projected = FieldProjector.Project(entity, selectedFields, jsonOptions);
                    if (projected is not null)
                    {
                        return Results.Json(projected, jsonOptions);
                    }
                }

                return Results.Json(entity, jsonOptions);
            }
            catch (Exception ex)
            {
                var errorResult = await EndpointHelpers.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.GetById, ex, id);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }
}
