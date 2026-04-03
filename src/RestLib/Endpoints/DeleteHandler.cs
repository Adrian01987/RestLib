using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;

namespace RestLib.Endpoints;

/// <summary>
/// Handles DELETE requests to remove an entity by ID.
/// </summary>
internal static class DeleteHandler
{
    /// <summary>
    /// Creates the delegate for the Delete endpoint.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="entityName">The unique entity name used in error messages.</param>
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
            var jsonOptions = EndpointHelpers.GetJsonOptions(httpContext);

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await EndpointHelpers.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.Delete, id);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;
            TEntity? entityToDelete = null;

            try
            {
                // OnRequestValidated hook
                var onValidatedResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
                if (onValidatedResult is not null) return onValidatedResult;

                // Fetch entity for hooks if pipeline exists
                if (pipeline is not null)
                {
                    entityToDelete = await repository.GetByIdAsync(id, ct);
                }

                // BeforePersist hook
                if (hookContext is not null) hookContext.Entity = entityToDelete;
                var beforePersistResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforePersistAsync);
                if (beforePersistResult is not null) return beforePersistResult;

                var deleted = await repository.DeleteAsync(id, ct);

                if (!deleted)
                {
                    return Responses.ProblemDetailsResult.NotFound(
                        entityName,
                        id!,
                        httpContext.Request.Path,
                        jsonOptions);
                }

                // AfterPersist hook
                var afterPersistResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteAfterPersistAsync);
                if (afterPersistResult is not null) return afterPersistResult;

                // BeforeResponse hook
                var beforeResponseResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseResult is not null) return beforeResponseResult;

                return Results.NoContent();
            }
            catch (Exception ex)
            {
                var errorResult = await EndpointHelpers.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.Delete, ex, id, entityToDelete);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }
}
