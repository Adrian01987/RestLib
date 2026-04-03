using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Configuration;
using RestLib.Hooks;

namespace RestLib.Endpoints;

/// <summary>
/// Handles PUT requests for full entity updates.
/// </summary>
internal static class UpdateHandler
{
    /// <summary>
    /// Creates the delegate for the Update endpoint.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="entityName">The unique entity name used in error messages.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<TKey, TEntity, IRepository<TEntity, TKey>, HttpContext, CancellationToken, Task<IResult>>
        CreateDelegate<TEntity, TKey>(
            RestLibEndpointConfiguration<TEntity, TKey> config,
            string entityName)
        where TEntity : class
    {
        return async (
            TKey id,
            TEntity entity,
            IRepository<TEntity, TKey> repository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var (jsonOptions, options) = EndpointHelpers.ResolveOptions(httpContext);

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await EndpointHelpers.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.Update, id, entity);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;
            // Entity might have been modified by hook
            if (hookContext is not null) entity = hookContext.Entity ?? entity;
            TEntity? originalEntity = null;

            try
            {
                // Validate entity using Data Annotations
                if (options.EnableValidation)
                {
                    var validationResult = Validation.EntityValidator.Validate(entity, options.JsonNamingPolicy);
                    if (!validationResult.IsValid)
                    {
                        return Responses.ProblemDetailsResult.ValidationFailed(
                            validationResult.Errors.ToDictionary(e => e.Key, e => e.Value),
                            httpContext.Request.Path,
                            jsonOptions);
                    }
                }

                // OnRequestValidated hook
                if (hookContext is not null) hookContext.Entity = entity;
                var onValidatedResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
                if (onValidatedResult is not null) return onValidatedResult;
                if (hookContext is not null) entity = hookContext.Entity ?? entity;

                // Check for ETag precondition (If-Match header)
                if (options.EnableETagSupport)
                {
                    var ifMatchHeader = httpContext.Request.Headers.IfMatch;
                    if (!Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty(ifMatchHeader))
                    {
                        // Get current entity to compare ETags
                        var current = await repository.GetByIdAsync(id, ct);
                        if (current is null)
                        {
                            return Responses.ProblemDetailsResult.NotFound(
                                entityName,
                                id!,
                                httpContext.Request.Path,
                                jsonOptions);
                        }

                        var etagGenerator = EndpointHelpers.ResolveETagGenerator(httpContext, jsonOptions);
                        var currentETag = etagGenerator.Generate(current);

                        if (!ETagComparer.IfMatchSucceeds(ifMatchHeader, currentETag))
                        {
                            return Responses.ProblemDetailsResult.PreconditionFailed(
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

                // BeforePersist hook — update existing context with original entity
                if (hookContext is not null)
                {
                    hookContext.Entity = entity;
                    hookContext.SetOriginalEntity(originalEntity);
                }

                var beforePersistResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforePersistAsync);
                if (beforePersistResult is not null) return beforePersistResult;
                if (hookContext is not null) entity = hookContext.Entity ?? entity;

                var updated = await repository.UpdateAsync(id, entity, ct);

                if (updated is null)
                {
                    return Responses.ProblemDetailsResult.NotFound(
                        entityName,
                        id!,
                        httpContext.Request.Path,
                        jsonOptions);
                }

                // AfterPersist hook
                if (hookContext is not null) hookContext.Entity = updated;
                var afterPersistResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteAfterPersistAsync);
                if (afterPersistResult is not null) return afterPersistResult;

                // Add ETag header when enabled
                if (options.EnableETagSupport)
                {
                    var etagGenerator = EndpointHelpers.ResolveETagGenerator(httpContext, jsonOptions);
                    httpContext.Response.Headers.ETag = etagGenerator.Generate(updated);
                }

                // BeforeResponse hook
                var beforeResponseResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseResult is not null) return beforeResponseResult;

                return Results.Json(updated, jsonOptions);
            }
            catch (Exception ex)
            {
                var errorResult = await EndpointHelpers.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.Update, ex, id, entity);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }
}
