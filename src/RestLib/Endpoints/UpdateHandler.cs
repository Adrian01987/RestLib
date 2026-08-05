using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Hypermedia;
using RestLib.Logging;
using RestLib.Mapping;
using RestLib.Validation;

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
    /// <param name="entityName">The clean entity type name used in error messages (e.g., "Product").</param>
    /// <returns>The request delegate.</returns>
    internal static Func<TKey, TEntity, IRepository<TEntity, TKey>, HttpContext, CancellationToken, Task<IResult>>
        CreateDelegate<TEntity, TKey>(
            RestLibEndpointConfiguration<TEntity, TKey> config,
            string entityName)
        where TEntity : class
        where TKey : notnull
    {
        return async (
            TKey id,
            TEntity entity,
            IRepository<TEntity, TKey> repository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Update");

            RestLibLogMessages.UpdateRequestReceived(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.Update, id, entity, logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;
            // Entity might have been modified by hook
            if (hookContext is not null) entity = hookContext.Entity ?? entity;
            TEntity? originalEntity = null;

            try
            {
                _ = EntityKeyHelper.TrySetEntityKeyParts(entity, id, config.KeyRouteParts);

                // Validate entity using Data Annotations
                if (options.EnableValidation)
                {
                    var validationResult = RestLibResourceValidator.Validate(entity, config, options.JsonNamingPolicy);
                    if (!validationResult.IsValid)
                    {
                        return Responses.ProblemDetailsResult.ValidationFailed(
                            validationResult.Errors,
                            httpContext.Request.Path,
                            jsonOptions,
                            logger,
                            options);
                    }
                }

                // OnRequestValidated hook
                var validatedStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, entity, p => p.ExecuteOnRequestValidatedAsync);
                if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;
                entity = validatedStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(entity, id, config.KeyRouteParts);

                // Check for ETag precondition (If-Match header)
                var (etagEntity, etagError) = await ETagHelper.CheckIfMatchPreconditionAsync(
                    httpContext, repository, id, entityName, options, jsonOptions, ct, logger);
                if (etagError is not null) return etagError;
                if (etagEntity is not null) originalEntity = etagEntity;

                var ifMatchPrecondition = ETagHelper.CreateIfMatchPrecondition<TEntity>(httpContext, options);
                if (ifMatchPrecondition is not null && repository is not IConditionalWriteRepository<TEntity, TKey>)
                {
                    return ETagHelper.ConditionalWriteNotSupported(httpContext, jsonOptions, options, logger);
                }

                // Fetch original entity if not already fetched (for hooks)
                if (originalEntity is null && pipeline is not null)
                {
                    originalEntity = await repository.GetByIdAsync(id, ct);
                }

                // BeforePersist hook — update existing context with original entity
                if (hookContext is not null) hookContext.SetOriginalEntity(originalEntity);

                var beforePersistStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, entity, p => p.ExecuteBeforePersistAsync);
                if (beforePersistStage.EarlyResult is not null) return beforePersistStage.EarlyResult;
                entity = beforePersistStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(entity, id, config.KeyRouteParts);

                TEntity? updated;
                if (ifMatchPrecondition is null)
                {
                    updated = await repository.UpdateAsync(id, entity, ct);
                }
                else
                {
                    var conditionalResult = await ((IConditionalWriteRepository<TEntity, TKey>)repository)
                        .UpdateConditionallyAsync(id, entity, ifMatchPrecondition, ct);
                    var conditionalError = ETagHelper.ToErrorResult(
                        conditionalResult,
                        httpContext,
                        id,
                        entityName,
                        config.KeyRouteParts,
                        jsonOptions,
                        options,
                        logger);
                    if (conditionalError is not null) return conditionalError;
                    updated = conditionalResult.Entity!;
                }

                if (updated is null)
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

                // AfterPersist hook
                var afterPersistStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, updated, p => p.ExecuteAfterPersistAsync);
                if (afterPersistStage.EarlyResult is not null) return afterPersistStage.EarlyResult;
                updated = afterPersistStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(updated, id, config.KeyRouteParts);

                // BeforeResponse hook
                var beforeResponseStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, updated, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseStage.EarlyResult is not null) return beforeResponseStage.EarlyResult;
                updated = beforeResponseStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(updated, id, config.KeyRouteParts);

                // Generate the ETag from the final response representation.
                if (options.EnableETagSupport)
                {
                    var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
                    httpContext.Response.Headers.ETag = etagGenerator.Generate(updated);
                }

                // Inject HATEOAS links into updated entity response
                if (options.EnableHateoas)
                {
                    var collectionPath = HateoasLinkBuilder.GetCollectionPath(httpContext.Request.Path, isCollectionEndpoint: false, config.KeyRouteParts.Count);
                    var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TEntity, TKey>>();
                    var customLinks = customLinksProvider?.GetLinks(updated, id);
                    var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, id, config, customLinks);
                    var entityWithLinks = HateoasHelper.EntityWithLinks<TEntity, TKey>(updated, links, jsonOptions);
                    return Results.Json(entityWithLinks, jsonOptions);
                }

                return Results.Json(updated, jsonOptions);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Update), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.Update, ex, id, entity, logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    /// <summary>
    /// Creates the delegate for the mapped Update endpoint.
    /// </summary>
    /// <typeparam name="TApiModel">The API model type.</typeparam>
    /// <typeparam name="TDbModel">The DB model type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="entityName">The API entity name used in error messages.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<TKey, TApiModel, HttpContext, CancellationToken, Task<IResult>>
        CreateMappedDelegate<TApiModel, TDbModel, TKey>(
            RestLibEndpointConfiguration<TApiModel, TDbModel, TKey> config,
            string entityName)
        where TApiModel : class
        where TDbModel : class
        where TKey : notnull
    {
        return async (
            id,
            apiEntity,
            httpContext,
            ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Update");
            var repository = httpContext.RequestServices.GetRequiredService<IRepository<TDbModel, TKey>>();
            var mapper = RestLibMapperResolver.Resolve<TApiModel, TDbModel>(
                httpContext.RequestServices,
                config.MapperName,
                config.UseAutoMapper,
                config.ResourceName);

            RestLibLogMessages.UpdateRequestReceived(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            if (config.UsesDbModelHooks)
            {
                var dbEntity = mapper.ToDb(apiEntity);
                var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TDbModel, TKey>(
                    config.DbModelHooks,
                    httpContext,
                    RestLibOperation.Update,
                    id,
                    dbEntity,
                    logger: logger);
                if (pipelineEarlyResult is not null) return pipelineEarlyResult;
                if (hookContext is not null)
                {
                    dbEntity = hookContext.Entity ?? dbEntity;
                    apiEntity = mapper.ToApi(dbEntity);
                }

                try
                {
                    return await ExecuteMappedUpdateAsync<TApiModel, TDbModel, TDbModel, TKey>(
                        id,
                        apiEntity,
                        dbEntity,
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
                    RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Update), ex);
                    var errorResult = await HookHelper.HandleErrorHookAsync(
                        pipeline,
                        httpContext,
                        RestLibOperation.Update,
                        ex,
                        id,
                        dbEntity,
                        logger);
                    if (errorResult is not null) return errorResult;
                    throw;
                }
            }

            var (apiPipeline, apiHookContext, apiPipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TApiModel, TKey>(
                config.Hooks,
                httpContext,
                RestLibOperation.Update,
                id,
                apiEntity,
                logger: logger);
            if (apiPipelineEarlyResult is not null) return apiPipelineEarlyResult;
            if (apiHookContext is not null)
            {
                apiEntity = apiHookContext.Entity ?? apiEntity;
            }

            try
            {
                return await ExecuteMappedUpdateAsync<TApiModel, TDbModel, TApiModel, TKey>(
                    id,
                    apiEntity,
                    mapper.ToDb(apiEntity),
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
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Update), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(
                    apiPipeline,
                    httpContext,
                    RestLibOperation.Update,
                    ex,
                    id,
                    apiEntity,
                    logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    private static async Task<IResult> ExecuteMappedUpdateAsync<TApiModel, TDbModel, THookModel, TKey>(
        TKey id,
        TApiModel apiEntity,
        TDbModel dbEntity,
        IRepository<TDbModel, TKey> repository,
        IRestLibMapper<TApiModel, TDbModel> mapper,
        HttpContext httpContext,
        CancellationToken ct,
        JsonSerializerOptions jsonOptions,
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
        TDbModel? originalDb = null;
        TApiModel? originalApi = null;

        _ = EntityKeyHelper.TrySetEntityKeyParts(dbEntity, id, config.KeyRouteParts);
        apiEntity = mapper.ToApi(dbEntity);

        if (options.EnableValidation)
        {
            var validationResult = RestLibResourceValidator.Validate(apiEntity, config, options.JsonNamingPolicy);
            if (!validationResult.IsValid)
            {
                return Responses.ProblemDetailsResult.ValidationFailed(
                    validationResult.Errors,
                    httpContext.Request.Path,
                    jsonOptions,
                    logger,
                    options);
            }
        }

        if (hookContext is not null)
        {
            hookContext.Entity = typeof(THookModel) == typeof(TDbModel)
                ? (THookModel)(object)dbEntity
                : (THookModel)(object)apiEntity;
        }

        var onValidatedResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
        if (onValidatedResult is not null) return onValidatedResult;

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                dbEntity = (TDbModel)(object)(hookContext.Entity ?? (THookModel)(object)dbEntity);
                _ = EntityKeyHelper.TrySetEntityKeyParts(dbEntity, id, config.KeyRouteParts);
                apiEntity = mapper.ToApi(dbEntity);
            }
            else
            {
                apiEntity = (TApiModel)(object)(hookContext.Entity ?? (THookModel)(object)apiEntity);
                dbEntity = mapper.ToDb(apiEntity);
                _ = EntityKeyHelper.TrySetEntityKeyParts(dbEntity, id, config.KeyRouteParts);
                apiEntity = mapper.ToApi(dbEntity);
            }
        }

        if (options.EnableValidation)
        {
            var validationResult = RestLibResourceValidator.Validate(apiEntity, config, options.JsonNamingPolicy);
            if (!validationResult.IsValid)
            {
                return Responses.ProblemDetailsResult.ValidationFailed(
                    validationResult.Errors,
                    httpContext.Request.Path,
                    jsonOptions,
                    logger,
                    options);
            }
        }

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
            originalDb = etagDb;
            originalApi = etagApi;
        }

        var ifMatchPrecondition = ETagHelper.CreateIfMatchPrecondition<TApiModel, TDbModel>(
            httpContext,
            options,
            mapper);
        if (ifMatchPrecondition is not null && repository is not IConditionalWriteRepository<TDbModel, TKey>)
        {
            return ETagHelper.ConditionalWriteNotSupported(httpContext, jsonOptions, options, logger);
        }

        if (originalDb is null && pipeline is not null)
        {
            originalDb = await repository.GetByIdAsync(id, ct);
            originalApi = originalDb is not null ? mapper.ToApi(originalDb) : null;
        }

        if (hookContext is not null)
        {
            hookContext.Entity = typeof(THookModel) == typeof(TDbModel)
                ? (THookModel)(object)dbEntity
                : (THookModel)(object)apiEntity;
            hookContext.SetOriginalEntity(
                typeof(THookModel) == typeof(TDbModel)
                    ? (THookModel?)(object?)originalDb
                    : (THookModel?)(object?)originalApi);
        }

        var beforePersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforePersistAsync);
        if (beforePersistResult is not null) return beforePersistResult;

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                dbEntity = (TDbModel)(object)(hookContext.Entity ?? (THookModel)(object)dbEntity);
            }
            else
            {
                apiEntity = (TApiModel)(object)(hookContext.Entity ?? (THookModel)(object)apiEntity);
                dbEntity = mapper.ToDb(apiEntity);
            }
        }

        _ = EntityKeyHelper.TrySetEntityKeyParts(dbEntity, id, config.KeyRouteParts);

        TDbModel? updatedDb;
        if (ifMatchPrecondition is null)
        {
            updatedDb = await repository.UpdateAsync(id, dbEntity, ct);
        }
        else
        {
            var conditionalResult = await ((IConditionalWriteRepository<TDbModel, TKey>)repository)
                .UpdateConditionallyAsync(id, dbEntity, ifMatchPrecondition, ct);
            var conditionalError = ETagHelper.ToErrorResult(
                conditionalResult,
                httpContext,
                id,
                entityName,
                config.KeyRouteParts,
                jsonOptions,
                options,
                logger);
            if (conditionalError is not null) return conditionalError;
            updatedDb = conditionalResult.Entity!;
        }
        if (updatedDb is null)
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

        var updatedApi = mapper.ToApi(updatedDb);

        if (hookContext is not null)
        {
            hookContext.Entity = typeof(THookModel) == typeof(TDbModel)
                ? (THookModel)(object)updatedDb
                : (THookModel)(object)updatedApi;
        }

        var afterPersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteAfterPersistAsync);
        if (afterPersistResult is not null) return afterPersistResult;

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                updatedDb = (TDbModel)(object)(hookContext.Entity ?? (THookModel)(object)updatedDb);
                _ = EntityKeyHelper.TrySetEntityKeyParts(updatedDb, id, config.KeyRouteParts);
                updatedApi = mapper.ToApi(updatedDb);
            }
            else
            {
                updatedApi = (TApiModel)(object)(hookContext.Entity ?? (THookModel)(object)updatedApi);
                _ = EntityKeyHelper.TrySetEntityKeyParts(updatedApi, id, config.KeyRouteParts);
            }
        }

        _ = EntityKeyHelper.TrySetEntityKeyParts(updatedApi, id, config.KeyRouteParts);

        if (hookContext is not null)
        {
            hookContext.Entity = typeof(THookModel) == typeof(TDbModel)
                ? (THookModel)(object)updatedDb
                : (THookModel)(object)updatedApi;
        }

        var beforeResponseResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
        if (beforeResponseResult is not null) return beforeResponseResult;

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                updatedDb = (TDbModel)(object)(hookContext.Entity ?? (THookModel)(object)updatedDb);
                _ = EntityKeyHelper.TrySetEntityKeyParts(updatedDb, id, config.KeyRouteParts);
                updatedApi = mapper.ToApi(updatedDb);
            }
            else
            {
                updatedApi = (TApiModel)(object)(hookContext.Entity ?? (THookModel)(object)updatedApi);
                _ = EntityKeyHelper.TrySetEntityKeyParts(updatedApi, id, config.KeyRouteParts);
            }
        }

        _ = EntityKeyHelper.TrySetEntityKeyParts(updatedApi, id, config.KeyRouteParts);

        if (options.EnableETagSupport)
        {
            var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
            httpContext.Response.Headers.ETag = etagGenerator.Generate(updatedApi);
        }

        if (options.EnableHateoas)
        {
            var collectionPath = HateoasLinkBuilder.GetCollectionPath(httpContext.Request.Path, isCollectionEndpoint: false, config.KeyRouteParts.Count);
            var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TApiModel, TKey>>();
            var customLinks = customLinksProvider?.GetLinks(updatedApi, id);
            var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, id, config, customLinks);
            var entityWithLinks = HateoasHelper.EntityWithLinks<TApiModel, TKey>(updatedApi, links, jsonOptions);
            return Results.Json(entityWithLinks, jsonOptions);
        }

        return Results.Json(updatedApi, jsonOptions);
    }
}
