using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Logging;
using RestLib.Mapping;

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
    /// <param name="entityName">The clean entity type name used in error messages (e.g., "Product").</param>
    /// <returns>The request delegate.</returns>
    internal static Func<TKey, IRepository<TEntity, TKey>, HttpContext, CancellationToken, Task<IResult>>
        CreateDelegate<TEntity, TKey>(
            RestLibEndpointConfiguration<TEntity, TKey> config,
            string entityName)
        where TEntity : class
        where TKey : notnull
    {
        return async (
            TKey id,
            IRepository<TEntity, TKey> repository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Delete");

            RestLibLogMessages.DeleteRequestReceived(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.Delete, id, logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;
            TEntity? entityToDelete = null;

            try
            {
                // OnRequestValidated hook
                var onValidatedResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
                if (onValidatedResult is not null) return onValidatedResult;

                // Check for ETag precondition (If-Match header)
                var (etagEntity, etagError) = await ETagHelper.CheckIfMatchPreconditionAsync(
                    httpContext, repository, id, entityName, options, jsonOptions, ct, logger);
                if (etagError is not null) return etagError;
                if (etagEntity is not null) entityToDelete = etagEntity;

                // Fetch entity for hooks if pipeline exists and not already fetched
                if (entityToDelete is null && pipeline is not null)
                {
                    entityToDelete = await repository.GetByIdAsync(id, ct);
                }

                // BeforePersist hook
                if (hookContext is not null) hookContext.Entity = entityToDelete;
                var beforePersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforePersistAsync);
                if (beforePersistResult is not null) return beforePersistResult;

                var deleted = await repository.DeleteAsync(id, ct);

                if (!deleted)
                {
                    return Responses.ProblemDetailsResult.NotFound(
                        entityName,
                        id!,
                        config.KeyRouteParts,
                        httpContext.Request.Path,
                        jsonOptions,
                        logger);
                }

                RestLibLogMessages.EntityDeleted(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

                // AfterPersist hook
                var afterPersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteAfterPersistAsync);
                if (afterPersistResult is not null) return afterPersistResult;

                // BeforeResponse hook
                var beforeResponseResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseResult is not null) return beforeResponseResult;

                return Results.NoContent();
            }
            catch (Exception ex)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Delete), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.Delete, ex, id, entityToDelete, logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    /// <summary>
    /// Creates the delegate for the mapped Delete endpoint.
    /// </summary>
    /// <typeparam name="TApiModel">The API model type.</typeparam>
    /// <typeparam name="TDbModel">The DB model type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="entityName">The API entity name used in error messages.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<TKey, HttpContext, CancellationToken, Task<IResult>>
        CreateMappedDelegate<TApiModel, TDbModel, TKey>(
            RestLibEndpointConfiguration<TApiModel, TDbModel, TKey> config,
            string entityName)
        where TApiModel : class
        where TDbModel : class
        where TKey : notnull
    {
        return async (
            id,
            httpContext,
            ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Delete");
            var repository = httpContext.RequestServices.GetRequiredService<IRepository<TDbModel, TKey>>();
            var mapper = RestLibMapperResolver.Resolve<TApiModel, TDbModel>(
                httpContext.RequestServices,
                config.MapperName,
                config.UseAutoMapper,
                config.ResourceName);

            RestLibLogMessages.DeleteRequestReceived(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            if (config.UsesDbModelHooks)
            {
                var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TDbModel, TKey>(
                    config.DbModelHooks,
                    httpContext,
                    RestLibOperation.Delete,
                    id,
                    logger: logger);
                if (pipelineEarlyResult is not null) return pipelineEarlyResult;

                try
                {
                    return await ExecuteMappedDeleteAsync<TApiModel, TDbModel, TDbModel, TKey>(
                        id,
                        repository,
                        mapper,
                        httpContext,
                        ct,
                        jsonOptions,
                        options,
                        logger,
                        config,
                        entityName,
                        pipeline,
                        hookContext);
                }
                catch (Exception ex)
                {
                    RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Delete), ex);
                    var errorResult = await HookHelper.HandleErrorHookAsync(
                        pipeline,
                        httpContext,
                        RestLibOperation.Delete,
                        ex,
                        id,
                        logger: logger);
                    if (errorResult is not null) return errorResult;
                    throw;
                }
            }

            var (apiPipeline, apiHookContext, apiPipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TApiModel, TKey>(
                config.Hooks,
                httpContext,
                RestLibOperation.Delete,
                id,
                logger: logger);
            if (apiPipelineEarlyResult is not null) return apiPipelineEarlyResult;

            try
            {
                return await ExecuteMappedDeleteAsync<TApiModel, TDbModel, TApiModel, TKey>(
                    id,
                    repository,
                    mapper,
                    httpContext,
                    ct,
                    jsonOptions,
                    options,
                    logger,
                    config,
                    entityName,
                    apiPipeline,
                    apiHookContext);
            }
            catch (Exception ex)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Delete), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(
                    apiPipeline,
                    httpContext,
                    RestLibOperation.Delete,
                    ex,
                    id,
                    logger: logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    private static async Task<IResult> ExecuteMappedDeleteAsync<TApiModel, TDbModel, THookModel, TKey>(
        TKey id,
        IRepository<TDbModel, TKey> repository,
        IRestLibMapper<TApiModel, TDbModel> mapper,
        HttpContext httpContext,
        CancellationToken ct,
        System.Text.Json.JsonSerializerOptions jsonOptions,
        RestLibOptions options,
        Microsoft.Extensions.Logging.ILogger logger,
        RestLibEndpointConfiguration<TApiModel, TDbModel, TKey> config,
        string entityName,
        HookPipeline<THookModel, TKey>? pipeline,
        HookContext<THookModel, TKey>? hookContext)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class
        where TKey : notnull
    {
        var onValidatedResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
        if (onValidatedResult is not null) return onValidatedResult;

        TDbModel? entityToDeleteDb = null;
        TApiModel? entityToDeleteApi = null;

        var (etagDb, etagApi, etagError) = await ETagHelper.CheckIfMatchPreconditionAsync<TApiModel, TDbModel, TKey>(
            httpContext,
            repository,
            mapper,
            id,
            entityName,
            options,
            jsonOptions,
            ct,
            logger);
        if (etagError is not null) return etagError;
        if (etagDb is not null)
        {
            entityToDeleteDb = etagDb;
            entityToDeleteApi = etagApi;
        }

        if (entityToDeleteDb is null && pipeline is not null)
        {
            entityToDeleteDb = await repository.GetByIdAsync(id, ct);
            entityToDeleteApi = entityToDeleteDb is not null ? mapper.ToApi(entityToDeleteDb) : null;
        }

        if (hookContext is not null)
        {
            hookContext.Entity = typeof(THookModel) == typeof(TDbModel)
                ? (THookModel?)(object?)entityToDeleteDb
                : (THookModel?)(object?)entityToDeleteApi;
        }

        var beforePersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforePersistAsync);
        if (beforePersistResult is not null) return beforePersistResult;

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                entityToDeleteDb = (TDbModel?)(object?)hookContext.Entity;
                entityToDeleteApi = entityToDeleteDb is not null ? mapper.ToApi(entityToDeleteDb) : entityToDeleteApi;
            }
            else
            {
                entityToDeleteApi = (TApiModel?)(object?)hookContext.Entity;
            }
        }

        var deleted = await repository.DeleteAsync(id, ct);
        if (!deleted)
        {
            return Responses.ProblemDetailsResult.NotFound(
                entityName,
                id!,
                config.KeyRouteParts,
                httpContext.Request.Path,
                jsonOptions,
                logger);
        }

        RestLibLogMessages.EntityDeleted(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

        if (hookContext is not null)
        {
            hookContext.Entity = typeof(THookModel) == typeof(TDbModel)
                ? (THookModel?)(object?)entityToDeleteDb
                : (THookModel?)(object?)entityToDeleteApi;
        }

        var afterPersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteAfterPersistAsync);
        if (afterPersistResult is not null) return afterPersistResult;

        var beforeResponseResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
        if (beforeResponseResult is not null) return beforeResponseResult;

        return Results.NoContent();
    }
}
