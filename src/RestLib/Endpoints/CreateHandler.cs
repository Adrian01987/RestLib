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
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Create");

            RestLibLogMessages.CreateRequestReceived(logger);

            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.Create, entity: entity, logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;
            if (hookContext is not null) entity = hookContext.Entity ?? entity;

            var modelAdapter = EndpointModelAdapter<TEntity, TEntity>.Identity<TEntity>();
            var state = new EndpointModelState<TEntity, TEntity>(entity, entity);

            try
            {
                return await ExecuteCreateAsync<TEntity, TEntity, TEntity, TKey>(
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
                    pipeline,
                    hookContext);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Create), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(
                    pipeline,
                    httpContext,
                    RestLibOperation.Create,
                    ex,
                    entity: state.ApiModel,
                    logger: logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    /// <summary>
    /// Creates the delegate for the mapped Create endpoint.
    /// </summary>
    /// <typeparam name="TApiModel">The API model type.</typeparam>
    /// <typeparam name="TDbModel">The DB model type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<TApiModel, HttpContext, CancellationToken, Task<IResult>>
        CreateMappedDelegate<TApiModel, TDbModel, TKey>(
            RestLibEndpointConfiguration<TApiModel, TDbModel, TKey> config)
        where TApiModel : class
        where TDbModel : class
        where TKey : notnull
    {
        return async (
            apiEntity,
            httpContext,
            ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Create");
            var repository = httpContext.RequestServices.GetRequiredService<IRepository<TDbModel, TKey>>();
            var mapper = RestLibMapperResolver.Resolve<TApiModel, TDbModel>(
                httpContext.RequestServices,
                config.MapperName,
                config.UseAutoMapper,
                config.ResourceName);
            var modelAdapter = EndpointModelAdapter<TApiModel, TDbModel>.Mapped(mapper);

            RestLibLogMessages.CreateRequestReceived(logger);

            if (config.UsesDbModelHooks)
            {
                var dbEntity = modelAdapter.ToDb(apiEntity);
                var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TDbModel, TKey>(
                    config.DbModelHooks,
                    httpContext,
                    RestLibOperation.Create,
                    entity: dbEntity,
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
                    return await ExecuteCreateAsync<TApiModel, TDbModel, TDbModel, TKey>(
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
                        pipeline,
                        hookContext);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Create), ex);
                    var errorResult = await HookHelper.HandleErrorHookAsync(
                        pipeline,
                        httpContext,
                        RestLibOperation.Create,
                        ex,
                        entity: errorHookEntity,
                        logger: logger);
                    if (errorResult is not null) return errorResult;
                    throw;
                }
            }

            var (apiPipeline, apiHookContext, apiPipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TApiModel, TKey>(
                config.Hooks,
                httpContext,
                RestLibOperation.Create,
                entity: apiEntity,
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
                return await ExecuteCreateAsync<TApiModel, TDbModel, TApiModel, TKey>(
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
                    apiPipeline,
                    apiHookContext);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Create), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(
                    apiPipeline,
                    httpContext,
                    RestLibOperation.Create,
                    ex,
                    entity: apiErrorHookEntity,
                    logger: logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    private static async Task<IResult> ExecuteCreateAsync<TApiModel, TDbModel, THookModel, TKey>(
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
        HookPipeline<THookModel, TKey>? pipeline,
        HookContext<THookModel, TKey>? hookContext)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class
        where TKey : notnull
    {
        var problems = Responses.ProblemDetailsResult.CreateResponder(jsonOptions, logger, options);

        var validationError = Validate(state.ApiModel, config, httpContext, jsonOptions, options, problems);
        if (validationError is not null) return validationError;

        var validatedStage = await HookHelper.RunEntityHookStageAsync(
            pipeline,
            hookContext,
            GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel),
            p => p.ExecuteOnRequestValidatedAsync);
        if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;
        ApplyRequestHookEntity(state, modelAdapter, validatedStage.Entity, hooksUseDbModel);

        if (!modelAdapter.IsIdentity)
        {
            validationError = Validate(state.ApiModel, config, httpContext, jsonOptions, options, problems);
            if (validationError is not null) return validationError;
        }

        var beforePersistStage = await HookHelper.RunEntityHookStageAsync(
            pipeline,
            hookContext,
            GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel),
            p => p.ExecuteBeforePersistAsync);
        if (beforePersistStage.EarlyResult is not null) return beforePersistStage.EarlyResult;

        if (hooksUseDbModel)
        {
            state.DbModel = (TDbModel)(object)beforePersistStage.Entity;
        }
        else
        {
            state.ApiModel = (TApiModel)(object)beforePersistStage.Entity;
            state.DbModel = modelAdapter.ToDb(state.ApiModel);
        }

        state.DbModel = await repository.CreateAsync(state.DbModel, ct);
        state.ApiModel = modelAdapter.ToApi(state.DbModel);
        var createdId = EntityKeyHelper.GetEntityKey(state.ApiModel, config.KeySelector);

        var afterPersistStage = await HookHelper.RunEntityHookStageAsync(
            pipeline,
            hookContext,
            GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel),
            p => p.ExecuteAfterPersistAsync);
        if (afterPersistStage.EarlyResult is not null) return afterPersistStage.EarlyResult;
        ApplyResponseHookEntity(state, modelAdapter, afterPersistStage.Entity, hooksUseDbModel, createdId, config);

        var location = RequestUrlHelper.BuildRelativeResourcePath(
            httpContext.Request,
            EntityKeyHelper.FormatKeyPath(createdId!, config.KeyRouteParts));
        httpContext.Response.Headers.Location = location;

        RestLibLogMessages.EntityCreated(logger, createdId?.ToString() ?? string.Empty, location);

        var beforeResponseStage = await HookHelper.RunEntityHookStageAsync(
            pipeline,
            hookContext,
            GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel),
            p => p.ExecuteBeforeResponseAsync);
        if (beforeResponseStage.EarlyResult is not null) return beforeResponseStage.EarlyResult;
        ApplyResponseHookEntity(state, modelAdapter, beforeResponseStage.Entity, hooksUseDbModel, createdId, config);

        if (options.EnableETagSupport)
        {
            var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
            httpContext.Response.Headers.ETag = etagGenerator.Generate(state.ApiModel);
        }

        if (options.EnableHateoas && createdId is not null)
        {
            var collectionPath = httpContext.Request.Path.ToString();
            var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TApiModel, TKey>>();
            var customLinks = customLinksProvider?.GetLinks(state.ApiModel, createdId);
            var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, createdId, config, customLinks);
            var entityWithLinks = HateoasHelper.EntityWithLinks<TApiModel, TKey>(state.ApiModel, links, jsonOptions);
            return Results.Json(entityWithLinks, jsonOptions, statusCode: StatusCodes.Status201Created);
        }

        return Results.Json(state.ApiModel, jsonOptions, statusCode: StatusCodes.Status201Created);
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

    private static void ApplyRequestHookEntity<TApiModel, TDbModel, THookModel>(
        EndpointModelState<TApiModel, TDbModel> state,
        EndpointModelAdapter<TApiModel, TDbModel> modelAdapter,
        THookModel hookEntity,
        bool hooksUseDbModel)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class
    {
        if (hooksUseDbModel)
        {
            state.DbModel = (TDbModel)(object)hookEntity;
            state.ApiModel = modelAdapter.ToApi(state.DbModel);
        }
        else
        {
            state.ApiModel = (TApiModel)(object)hookEntity;
            state.DbModel = modelAdapter.ToDb(state.ApiModel);
        }
    }

    private static void ApplyResponseHookEntity<TApiModel, TDbModel, THookModel, TKey>(
        EndpointModelState<TApiModel, TDbModel> state,
        EndpointModelAdapter<TApiModel, TDbModel> modelAdapter,
        THookModel hookEntity,
        bool hooksUseDbModel,
        TKey? createdId,
        RestLibEndpointConfiguration<TApiModel, TKey> config)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class
        where TKey : notnull
    {
        if (hooksUseDbModel)
        {
            state.DbModel = (TDbModel)(object)hookEntity;
            if (createdId is not null)
            {
                _ = EntityKeyHelper.TrySetEntityKeyParts(state.DbModel, createdId, config.KeyRouteParts);
            }

            state.ApiModel = modelAdapter.ToApi(state.DbModel);
        }
        else
        {
            state.ApiModel = (TApiModel)(object)hookEntity;
        }

        if (createdId is not null)
        {
            _ = EntityKeyHelper.TrySetEntityKeyParts(state.ApiModel, createdId, config.KeyRouteParts);
        }
    }
}
