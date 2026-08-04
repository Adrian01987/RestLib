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

                if (pipeline is not null && entityToDelete is null)
                {
                    return Responses.ProblemDetailsResult.NotFound(
                        entityName,
                        id!,
                        config.KeyRouteParts,
                        httpContext.Request.Path,
                        jsonOptions,
                        logger,
                        options);
                }

                if (entityToDelete is not null)
                {
                    var validatedStage = await HookHelper.RunEntityHookStageAsync(
                        pipeline, hookContext, entityToDelete, p => p.ExecuteOnRequestValidatedAsync);
                    if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;
                    entityToDelete = validatedStage.Entity;
                    _ = EntityKeyHelper.TrySetEntityKeyParts(entityToDelete, id, config.KeyRouteParts);
                }

                // BeforePersist hook
                if (entityToDelete is not null)
                {
                    var beforePersistStage = await HookHelper.RunEntityHookStageAsync(
                        pipeline, hookContext, entityToDelete, p => p.ExecuteBeforePersistAsync);
                    if (beforePersistStage.EarlyResult is not null) return beforePersistStage.EarlyResult;
                    entityToDelete = beforePersistStage.Entity;
                    _ = EntityKeyHelper.TrySetEntityKeyParts(entityToDelete, id, config.KeyRouteParts);
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
                        logger,
                        options);
                }

                RestLibLogMessages.EntityDeleted(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

                // AfterPersist hook
                if (entityToDelete is not null)
                {
                    var afterPersistStage = await HookHelper.RunEntityHookStageAsync(
                        pipeline, hookContext, entityToDelete, p => p.ExecuteAfterPersistAsync);
                    if (afterPersistStage.EarlyResult is not null) return afterPersistStage.EarlyResult;
                    entityToDelete = afterPersistStage.Entity;
                    _ = EntityKeyHelper.TrySetEntityKeyParts(entityToDelete, id, config.KeyRouteParts);
                }

                // BeforeResponse hook
                if (entityToDelete is not null)
                {
                    var beforeResponseStage = await HookHelper.RunEntityHookStageAsync(
                        pipeline, hookContext, entityToDelete, p => p.ExecuteBeforeResponseAsync);
                    if (beforeResponseStage.EarlyResult is not null) return beforeResponseStage.EarlyResult;
                    entityToDelete = beforeResponseStage.Entity;
                }

                return Results.NoContent();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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

        if (pipeline is not null && entityToDeleteDb is null)
        {
            return Responses.ProblemDetailsResult.NotFound(
                entityName,
                id!,
                config.KeyRouteParts,
                httpContext.Request.Path,
                jsonOptions,
                logger,
                options);
        }

        var validatedHookEntity = typeof(THookModel) == typeof(TDbModel)
            ? (THookModel?)(object?)entityToDeleteDb
            : (THookModel?)(object?)entityToDeleteApi;
        if (validatedHookEntity is not null)
        {
            var validatedStage = await HookHelper.RunEntityHookStageAsync(
                pipeline, hookContext, validatedHookEntity, p => p.ExecuteOnRequestValidatedAsync);
            if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;

            if (typeof(THookModel) == typeof(TDbModel))
            {
                entityToDeleteDb = (TDbModel)(object)validatedStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(entityToDeleteDb, id, config.KeyRouteParts);
                entityToDeleteApi = mapper.ToApi(entityToDeleteDb);
            }
            else
            {
                entityToDeleteApi = (TApiModel)(object)validatedStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(entityToDeleteApi, id, config.KeyRouteParts);
            }
        }

        var beforePersistHookEntity = typeof(THookModel) == typeof(TDbModel)
            ? (THookModel?)(object?)entityToDeleteDb
            : (THookModel?)(object?)entityToDeleteApi;
        if (beforePersistHookEntity is not null)
        {
            var beforePersistStage = await HookHelper.RunEntityHookStageAsync(
                pipeline, hookContext, beforePersistHookEntity, p => p.ExecuteBeforePersistAsync);
            if (beforePersistStage.EarlyResult is not null) return beforePersistStage.EarlyResult;

            if (typeof(THookModel) == typeof(TDbModel))
            {
                entityToDeleteDb = (TDbModel)(object)beforePersistStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(entityToDeleteDb, id, config.KeyRouteParts);
                entityToDeleteApi = mapper.ToApi(entityToDeleteDb);
            }
            else
            {
                entityToDeleteApi = (TApiModel)(object)beforePersistStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(entityToDeleteApi, id, config.KeyRouteParts);
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
                logger,
                options);
        }

        RestLibLogMessages.EntityDeleted(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

        var afterPersistHookEntity = typeof(THookModel) == typeof(TDbModel)
            ? (THookModel?)(object?)entityToDeleteDb
            : (THookModel?)(object?)entityToDeleteApi;
        if (afterPersistHookEntity is not null)
        {
            var afterPersistStage = await HookHelper.RunEntityHookStageAsync(
                pipeline, hookContext, afterPersistHookEntity, p => p.ExecuteAfterPersistAsync);
            if (afterPersistStage.EarlyResult is not null) return afterPersistStage.EarlyResult;

            var beforeResponseHookEntity = afterPersistStage.Entity;
            _ = EntityKeyHelper.TrySetEntityKeyParts(beforeResponseHookEntity, id, config.KeyRouteParts);

            var beforeResponseStage = await HookHelper.RunEntityHookStageAsync(
                pipeline, hookContext, beforeResponseHookEntity, p => p.ExecuteBeforeResponseAsync);
            if (beforeResponseStage.EarlyResult is not null) return beforeResponseStage.EarlyResult;
        }

        return Results.NoContent();
    }
}
