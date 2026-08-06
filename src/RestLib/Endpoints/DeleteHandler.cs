using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

            RestLibLogMessages.DeleteRequestReceived(
                logger,
                entityName,
                EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks,
                httpContext,
                RestLibOperation.Delete,
                id,
                logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;

            var state = new OptionalEndpointModelState<TEntity, TEntity>();
            try
            {
                return await ExecuteDeleteAsync<TEntity, TEntity, TEntity, TKey>(
                    id,
                    state,
                    repository,
                    EndpointModelAdapter<TEntity, TEntity>.Identity<TEntity>(),
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
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Delete), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(
                    pipeline,
                    httpContext,
                    RestLibOperation.Delete,
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
            var modelAdapter = EndpointModelAdapter<TApiModel, TDbModel>.Mapped(mapper);

            RestLibLogMessages.DeleteRequestReceived(
                logger,
                entityName,
                EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

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
                    return await ExecuteDeleteAsync<TApiModel, TDbModel, TDbModel, TKey>(
                        id,
                        new OptionalEndpointModelState<TApiModel, TDbModel>(),
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
                return await ExecuteDeleteAsync<TApiModel, TDbModel, TApiModel, TKey>(
                    id,
                    new OptionalEndpointModelState<TApiModel, TDbModel>(),
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

    private static async Task<IResult> ExecuteDeleteAsync<TApiModel, TDbModel, THookModel, TKey>(
        TKey id,
        OptionalEndpointModelState<TApiModel, TDbModel> state,
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
            state.DbModel = etagDb;
            state.ApiModel = etagApi;
        }

        var ifMatchPrecondition = ETagHelper.CreateIfMatchPrecondition<TApiModel, TDbModel>(
            httpContext,
            options,
            modelAdapter.Mapper);
        if (ifMatchPrecondition is not null && repository is not IConditionalWriteRepository<TDbModel, TKey>)
        {
            return ETagHelper.ConditionalWriteNotSupported(httpContext, jsonOptions, options, logger);
        }

        if (state.DbModel is null && pipeline is not null)
        {
            state.DbModel = await repository.GetByIdAsync(id, ct);
            state.ApiModel = state.DbModel is not null ? modelAdapter.ToApi(state.DbModel) : null;
        }

        if (pipeline is not null && state.DbModel is null)
        {
            return problems.Create(Responses.ProblemDetailsFactory.NotFound(
                entityName,
                id!,
                config.KeyRouteParts,
                httpContext.Request.Path));
        }

        var validatedHookEntity = GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel);
        if (validatedHookEntity is not null)
        {
            var validatedStage = await HookHelper.RunEntityHookStageAsync(
                pipeline,
                hookContext,
                validatedHookEntity,
                p => p.ExecuteOnRequestValidatedAsync);
            if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;
            ApplyRequestHookEntity(state, modelAdapter, validatedStage.Entity, hooksUseDbModel, id, config);
        }

        var beforePersistHookEntity = GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel);
        if (beforePersistHookEntity is not null)
        {
            var beforePersistStage = await HookHelper.RunEntityHookStageAsync(
                pipeline,
                hookContext,
                beforePersistHookEntity,
                p => p.ExecuteBeforePersistAsync);
            if (beforePersistStage.EarlyResult is not null) return beforePersistStage.EarlyResult;
            ApplyRequestHookEntity(state, modelAdapter, beforePersistStage.Entity, hooksUseDbModel, id, config);
        }

        if (ifMatchPrecondition is null)
        {
            var deleted = await repository.DeleteAsync(id, ct);
            if (!deleted)
            {
                return problems.Create(Responses.ProblemDetailsFactory.NotFound(
                    entityName,
                    id!,
                    config.KeyRouteParts,
                    httpContext.Request.Path));
            }
        }
        else
        {
            var conditionalResult = await ((IConditionalWriteRepository<TDbModel, TKey>)repository)
                .DeleteConditionallyAsync(id, ifMatchPrecondition, ct);
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
            state.DbModel = conditionalResult.Entity!;
            state.ApiModel = modelAdapter.ToApi(state.DbModel);
        }

        RestLibLogMessages.EntityDeleted(
            logger,
            entityName,
            EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

        var afterPersistHookEntity = GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel);
        if (afterPersistHookEntity is not null)
        {
            var afterPersistStage = await HookHelper.RunEntityHookStageAsync(
                pipeline,
                hookContext,
                afterPersistHookEntity,
                p => p.ExecuteAfterPersistAsync);
            if (afterPersistStage.EarlyResult is not null) return afterPersistStage.EarlyResult;

            var beforeResponseHookEntity = afterPersistStage.Entity;
            _ = EntityKeyHelper.TrySetEntityKeyParts(beforeResponseHookEntity, id, config.KeyRouteParts);

            var beforeResponseStage = await HookHelper.RunEntityHookStageAsync(
                pipeline,
                hookContext,
                beforeResponseHookEntity,
                p => p.ExecuteBeforeResponseAsync);
            if (beforeResponseStage.EarlyResult is not null) return beforeResponseStage.EarlyResult;
        }

        return Results.NoContent();
    }

    private static THookModel? GetHookEntity<TApiModel, TDbModel, THookModel>(
        OptionalEndpointModelState<TApiModel, TDbModel> state,
        bool hooksUseDbModel)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class =>
        hooksUseDbModel
            ? (THookModel?)(object?)state.DbModel
            : (THookModel?)(object?)state.ApiModel;

    private static void ApplyRequestHookEntity<TApiModel, TDbModel, THookModel, TKey>(
        OptionalEndpointModelState<TApiModel, TDbModel> state,
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
            _ = EntityKeyHelper.TrySetEntityKeyParts(state.ApiModel, id, config.KeyRouteParts);
        }
    }
}
