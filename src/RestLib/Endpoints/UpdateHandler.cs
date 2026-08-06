using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

            RestLibLogMessages.UpdateRequestReceived(
                logger,
                entityName,
                EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks,
                httpContext,
                RestLibOperation.Update,
                id,
                entity,
                logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;
            if (hookContext is not null) entity = hookContext.Entity ?? entity;

            var modelAdapter = EndpointModelAdapter<TEntity, TEntity>.Identity<TEntity>();
            var state = new EndpointModelState<TEntity, TEntity>(entity, entity);
            try
            {
                return await ExecuteUpdateAsync<TEntity, TEntity, TEntity, TKey>(
                    id,
                    state,
                    repository,
                    modelAdapter,
                    hooksUseDbModel: false,
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
                    state.ApiModel,
                    logger);
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
            var modelAdapter = EndpointModelAdapter<TApiModel, TDbModel>.Mapped(mapper);

            RestLibLogMessages.UpdateRequestReceived(
                logger,
                entityName,
                EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            if (config.UsesDbModelHooks)
            {
                var dbEntity = modelAdapter.ToDb(apiEntity);
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
                    apiEntity = modelAdapter.ToApi(dbEntity);
                }

                var errorHookEntity = dbEntity;
                var state = new EndpointModelState<TApiModel, TDbModel>(apiEntity, dbEntity);
                try
                {
                    return await ExecuteUpdateAsync<TApiModel, TDbModel, TDbModel, TKey>(
                        id,
                        state,
                        repository,
                        modelAdapter,
                        hooksUseDbModel: true,
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
                        errorHookEntity,
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

            var apiErrorHookEntity = apiEntity;
            try
            {
                var state = new EndpointModelState<TApiModel, TDbModel>(apiEntity, modelAdapter.ToDb(apiEntity));
                return await ExecuteUpdateAsync<TApiModel, TDbModel, TApiModel, TKey>(
                    id,
                    state,
                    repository,
                    modelAdapter,
                    hooksUseDbModel: typeof(TApiModel) == typeof(TDbModel),
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
                    apiErrorHookEntity,
                    logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    private static async Task<IResult> ExecuteUpdateAsync<TApiModel, TDbModel, THookModel, TKey>(
        TKey id,
        EndpointModelState<TApiModel, TDbModel> state,
        IRepository<TDbModel, TKey> repository,
        EndpointModelAdapter<TApiModel, TDbModel> modelAdapter,
        bool hooksUseDbModel,
        HttpContext httpContext,
        CancellationToken ct,
        JsonSerializerOptions jsonOptions,
        RestLibOptions options,
        ILogger logger,
        RestLibEndpointConfiguration<TApiModel, TKey> config,
        string entityName,
        HookPipeline<THookModel, TKey>? pipeline,
        HookContext<THookModel, TKey>? hookContext)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class
        where TKey : notnull
    {
        var problems = Responses.ProblemDetailsResult.CreateResponder(jsonOptions, logger, options);
        TDbModel? originalDb = null;
        TApiModel? originalApi = null;

        _ = EntityKeyHelper.TrySetEntityKeyParts(state.DbModel, id, config.KeyRouteParts);
        state.ApiModel = modelAdapter.ToApi(state.DbModel);

        var validationError = Validate(state.ApiModel, config, httpContext, jsonOptions, options, problems);
        if (validationError is not null) return validationError;

        var validatedStage = await HookHelper.RunEntityHookStageAsync(
            pipeline,
            hookContext,
            GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel),
            p => p.ExecuteOnRequestValidatedAsync);
        if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;
        if (modelAdapter.IsIdentity || hookContext is not null)
        {
            ApplyRequestHookEntity(state, modelAdapter, validatedStage.Entity, hooksUseDbModel, id, config);
        }

        if (!modelAdapter.IsIdentity)
        {
            validationError = Validate(state.ApiModel, config, httpContext, jsonOptions, options, problems);
            if (validationError is not null) return validationError;
        }

        var (etagDb, etagApi, etagError) = await ETagHelper.CheckIfMatchPreconditionAsync<TApiModel, TDbModel, TKey>(
            httpContext,
            repository,
            modelAdapter.Mapper,
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
            modelAdapter.Mapper);
        if (ifMatchPrecondition is not null && repository is not IConditionalWriteRepository<TDbModel, TKey>)
        {
            return ETagHelper.ConditionalWriteNotSupported(httpContext, jsonOptions, options, logger);
        }

        if (originalDb is null && pipeline is not null)
        {
            originalDb = await repository.GetByIdAsync(id, ct);
            originalApi = originalDb is not null ? modelAdapter.ToApi(originalDb) : null;
        }

        if (hookContext is not null)
        {
            hookContext.SetOriginalEntity(
                hooksUseDbModel
                    ? (THookModel?)(object?)originalDb
                    : (THookModel?)(object?)originalApi);
        }

        var beforePersistStage = await HookHelper.RunEntityHookStageAsync(
            pipeline,
            hookContext,
            GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel),
            p => p.ExecuteBeforePersistAsync);
        if (beforePersistStage.EarlyResult is not null) return beforePersistStage.EarlyResult;

        if (modelAdapter.IsIdentity || hookContext is not null)
        {
            if (hooksUseDbModel)
            {
                state.DbModel = (TDbModel)(object)beforePersistStage.Entity;
            }
            else
            {
                state.ApiModel = (TApiModel)(object)beforePersistStage.Entity;
                state.DbModel = modelAdapter.ToDb(state.ApiModel);
            }
        }

        _ = EntityKeyHelper.TrySetEntityKeyParts(state.DbModel, id, config.KeyRouteParts);

        TDbModel? updatedDb;
        if (ifMatchPrecondition is null)
        {
            updatedDb = await repository.UpdateAsync(id, state.DbModel, ct);
        }
        else
        {
            var conditionalResult = await ((IConditionalWriteRepository<TDbModel, TKey>)repository)
                .UpdateConditionallyAsync(id, state.DbModel, ifMatchPrecondition, ct);
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
            return problems.Create(Responses.ProblemDetailsFactory.NotFound(
                entityName,
                id!,
                config.KeyRouteParts,
                httpContext.Request.Path));
        }

        state.DbModel = updatedDb;
        state.ApiModel = modelAdapter.ToApi(updatedDb);

        var afterPersistStage = await HookHelper.RunEntityHookStageAsync(
            pipeline,
            hookContext,
            GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel),
            p => p.ExecuteAfterPersistAsync);
        if (afterPersistStage.EarlyResult is not null) return afterPersistStage.EarlyResult;
        if (modelAdapter.IsIdentity || hookContext is not null)
        {
            ApplyResponseHookEntity(state, modelAdapter, afterPersistStage.Entity, hooksUseDbModel, id, config);
        }
        else
        {
            _ = EntityKeyHelper.TrySetEntityKeyParts(state.ApiModel, id, config.KeyRouteParts);
        }

        var beforeResponseStage = await HookHelper.RunEntityHookStageAsync(
            pipeline,
            hookContext,
            GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel),
            p => p.ExecuteBeforeResponseAsync);
        if (beforeResponseStage.EarlyResult is not null) return beforeResponseStage.EarlyResult;
        if (modelAdapter.IsIdentity || hookContext is not null)
        {
            ApplyResponseHookEntity(state, modelAdapter, beforeResponseStage.Entity, hooksUseDbModel, id, config);
        }
        else
        {
            _ = EntityKeyHelper.TrySetEntityKeyParts(state.ApiModel, id, config.KeyRouteParts);
        }

        if (options.EnableETagSupport)
        {
            var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
            httpContext.Response.Headers.ETag = etagGenerator.Generate(state.ApiModel);
        }

        if (options.EnableHateoas)
        {
            var collectionPath = HateoasLinkBuilder.GetCollectionPath(
                httpContext.Request.Path,
                isCollectionEndpoint: false,
                config.KeyRouteParts.Count);
            var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TApiModel, TKey>>();
            var customLinks = customLinksProvider?.GetLinks(state.ApiModel, id);
            var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, id, config, customLinks);
            var entityWithLinks = HateoasHelper.EntityWithLinks<TApiModel, TKey>(state.ApiModel, links, jsonOptions);
            return Results.Json(entityWithLinks, jsonOptions);
        }

        return Results.Json(state.ApiModel, jsonOptions);
    }

    private static IResult? Validate<TApiModel, TKey>(
        TApiModel apiModel,
        RestLibEndpointConfiguration<TApiModel, TKey> config,
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions,
        RestLibOptions options,
        Responses.ProblemDetailsResponder problems)
        where TApiModel : class
        where TKey : notnull
    {
        if (!options.EnableValidation)
        {
            return null;
        }

        var validationResult = RestLibResourceValidator.Validate(
            apiModel,
            config,
            jsonOptions.PropertyNamingPolicy);
        return validationResult.IsValid
            ? null
            : problems.Create(Responses.ProblemDetailsFactory.ValidationFailed(
                validationResult.Errors,
                httpContext.Request.Path));
    }

    private static THookModel GetHookEntity<TApiModel, TDbModel, THookModel>(
        EndpointModelState<TApiModel, TDbModel> state,
        bool hooksUseDbModel)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class =>
        hooksUseDbModel
            ? (THookModel)(object)state.DbModel
            : (THookModel)(object)state.ApiModel;

    private static void ApplyRequestHookEntity<TApiModel, TDbModel, THookModel, TKey>(
        EndpointModelState<TApiModel, TDbModel> state,
        EndpointModelAdapter<TApiModel, TDbModel> modelAdapter,
        THookModel hookEntity,
        bool hooksUseDbModel,
        TKey id,
        RestLibEndpointConfiguration<TApiModel, TKey> config)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class
        where TKey : notnull
    {
        if (hooksUseDbModel)
        {
            state.DbModel = (TDbModel)(object)hookEntity;
            _ = EntityKeyHelper.TrySetEntityKeyParts(state.DbModel, id, config.KeyRouteParts);
            state.ApiModel = modelAdapter.ToApi(state.DbModel);
        }
        else
        {
            state.ApiModel = (TApiModel)(object)hookEntity;
            state.DbModel = modelAdapter.ToDb(state.ApiModel);
            _ = EntityKeyHelper.TrySetEntityKeyParts(state.DbModel, id, config.KeyRouteParts);
            state.ApiModel = modelAdapter.ToApi(state.DbModel);
        }
    }

    private static void ApplyResponseHookEntity<TApiModel, TDbModel, THookModel, TKey>(
        EndpointModelState<TApiModel, TDbModel> state,
        EndpointModelAdapter<TApiModel, TDbModel> modelAdapter,
        THookModel hookEntity,
        bool hooksUseDbModel,
        TKey id,
        RestLibEndpointConfiguration<TApiModel, TKey> config)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class
        where TKey : notnull
    {
        if (hooksUseDbModel)
        {
            state.DbModel = (TDbModel)(object)hookEntity;
            _ = EntityKeyHelper.TrySetEntityKeyParts(state.DbModel, id, config.KeyRouteParts);
            state.ApiModel = modelAdapter.ToApi(state.DbModel);
        }
        else
        {
            state.ApiModel = (TApiModel)(object)hookEntity;
        }

        _ = EntityKeyHelper.TrySetEntityKeyParts(state.ApiModel, id, config.KeyRouteParts);
    }
}
