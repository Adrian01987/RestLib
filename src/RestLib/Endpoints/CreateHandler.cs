using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Configuration;

namespace RestLib.Endpoints;

/// <summary>
/// Handles POST requests to create a new entity.
/// </summary>
internal static class CreateHandler
{
    /// <summary>
    /// Creates the delegate for the Create endpoint.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<TEntity, IRepository<TEntity, TKey>, HttpContext, CancellationToken, Task<IResult>>
        CreateDelegate<TEntity, TKey>(RestLibEndpointConfiguration<TEntity, TKey> config)
        where TEntity : class
        where TKey : notnull
    {
        return async (
            TEntity entity,
            IRepository<TEntity, TKey> repository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.Create, entity: entity);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;
            // Entity might have been modified by hook
            if (hookContext is not null) entity = hookContext.Entity ?? entity;

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
                var onValidatedResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
                if (onValidatedResult is not null) return onValidatedResult;
                if (hookContext is not null) entity = hookContext.Entity ?? entity;

                // BeforePersist hook
                if (hookContext is not null) hookContext.Entity = entity;
                var beforePersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforePersistAsync);
                if (beforePersistResult is not null) return beforePersistResult;
                if (hookContext is not null) entity = hookContext.Entity ?? entity;

                var created = await repository.CreateAsync(entity, ct);

                // AfterPersist hook
                if (hookContext is not null) hookContext.Entity = created;
                var afterPersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteAfterPersistAsync);
                if (afterPersistResult is not null) return afterPersistResult;

                // Extract ID from created entity and set Location header
                var createdId = EntityKeyHelper.GetEntityKey(created, config.KeySelector);
                var location = $"{httpContext.Request.Path}/{createdId}";
                httpContext.Response.Headers.Location = location;

                // Add ETag header when enabled
                if (options.EnableETagSupport)
                {
                    var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
                    httpContext.Response.Headers.ETag = etagGenerator.Generate(created);
                }

                // BeforeResponse hook
                var beforeResponseResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseResult is not null) return beforeResponseResult;

                return Results.Json(created, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (Exception ex)
            {
                var errorResult = await HookHelper.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.Create, ex, entity: entity);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }
}
