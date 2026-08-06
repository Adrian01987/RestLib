using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Configuration;
using RestLib.FieldSelection;
using RestLib.Hooks;
using RestLib.Hypermedia;
using RestLib.Logging;
using RestLib.Mapping;

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
        where TKey : notnull
    {
        return async (
            TKey id,
            IRepository<TEntity, TKey> repository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.GetById");

            RestLibLogMessages.GetByIdRequestReceived(
                logger,
                entityName,
                EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks,
                httpContext,
                RestLibOperation.GetById,
                id,
                logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;

            try
            {
                return await ExecuteGetByIdAsync<TEntity, TEntity, TEntity, TKey>(
                    id,
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
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.GetById), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(
                    pipeline,
                    httpContext,
                    RestLibOperation.GetById,
                    ex,
                    id,
                    logger: logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    /// <summary>
    /// Creates the delegate for the mapped GetById endpoint.
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
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.GetById");
            var repository = httpContext.RequestServices.GetRequiredService<IRepository<TDbModel, TKey>>();
            var mapper = RestLibMapperResolver.Resolve<TApiModel, TDbModel>(
                httpContext.RequestServices,
                config.MapperName,
                config.UseAutoMapper,
                config.ResourceName);
            var modelAdapter = EndpointModelAdapter<TApiModel, TDbModel>.Mapped(mapper);

            RestLibLogMessages.GetByIdRequestReceived(
                logger,
                entityName,
                EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            if (config.UsesDbModelHooks)
            {
                var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TDbModel, TKey>(
                    config.DbModelHooks,
                    httpContext,
                    RestLibOperation.GetById,
                    id,
                    logger: logger);
                if (pipelineEarlyResult is not null) return pipelineEarlyResult;

                try
                {
                    return await ExecuteGetByIdAsync<TApiModel, TDbModel, TDbModel, TKey>(
                        id,
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
                    RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.GetById), ex);
                    var errorResult = await HookHelper.HandleErrorHookAsync(
                        pipeline,
                        httpContext,
                        RestLibOperation.GetById,
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
                RestLibOperation.GetById,
                id,
                logger: logger);
            if (apiPipelineEarlyResult is not null) return apiPipelineEarlyResult;

            try
            {
                return await ExecuteGetByIdAsync<TApiModel, TDbModel, TApiModel, TKey>(
                    id,
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
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.GetById), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(
                    apiPipeline,
                    httpContext,
                    RestLibOperation.GetById,
                    ex,
                    id,
                    logger: logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    private static async Task<IResult> ExecuteGetByIdAsync<TApiModel, TDbModel, THookModel, TKey>(
        TKey id,
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
        var selectedFieldsResult = ParseSelectedFields(config, httpContext, problems);
        if (selectedFieldsResult.Error is not null) return selectedFieldsResult.Error;
        var selectedFields = selectedFieldsResult.Fields;

        TDbModel? dbEntity;
        if (modelAdapter.IsIdentity &&
            selectedFields.Count > 0 &&
            ShouldUseProjectionPushdown(options, config) &&
            repository is IFieldSelectionProjectionRepository<TDbModel, TKey> projectionRepository)
        {
            dbEntity = await projectionRepository.GetByIdProjectedAsync(id, selectedFields, ct: ct)
                ?? await repository.GetByIdAsync(id, ct);
        }
        else
        {
            dbEntity = await repository.GetByIdAsync(id, ct);
        }

        if (dbEntity is null)
        {
            return problems.Create(Responses.ProblemDetailsFactory.NotFound(
                entityName,
                id!,
                config.KeyRouteParts,
                httpContext.Request.Path));
        }

        var state = new EndpointModelState<TApiModel, TDbModel>(modelAdapter.ToApi(dbEntity), dbEntity);

        var validatedStage = await HookHelper.RunEntityHookStageAsync(
            pipeline,
            hookContext,
            GetHookEntity<TApiModel, TDbModel, THookModel>(state, hooksUseDbModel),
            p => p.ExecuteOnRequestValidatedAsync);
        if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;
        if (modelAdapter.IsIdentity || hookContext is not null)
        {
            ApplyResponseHookEntity(state, modelAdapter, validatedStage.Entity, hooksUseDbModel, id, config);
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
            var etag = etagGenerator.Generate(state.ApiModel);

            var ifNoneMatch = httpContext.Request.Headers.IfNoneMatch;
            if (!ETagComparer.IfNoneMatchSucceeds(ifNoneMatch, etag))
            {
                RestLibLogMessages.GetByIdNotModified(
                    logger,
                    entityName,
                    EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));
                httpContext.Response.Headers.ETag = etag;
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            httpContext.Response.Headers.ETag = etag;
        }

        if (selectedFields.Count > 0)
        {
            var projected = FieldProjector.Project(
                state.ApiModel,
                selectedFields,
                jsonOptions,
                config.FieldSelectionConfiguration.ResponseShape);
            if (projected is not null)
            {
                if (options.EnableHateoas)
                {
                    var links = BuildLinks(state.ApiModel, id, config, httpContext);
                    HateoasHelper.InjectLinksIntoProjected(projected, links, jsonOptions);
                }

                return Results.Json(projected, jsonOptions);
            }
        }

        if (options.EnableHateoas)
        {
            var links = BuildLinks(state.ApiModel, id, config, httpContext);
            var entityWithLinks = HateoasHelper.EntityWithLinks<TApiModel, TKey>(state.ApiModel, links, jsonOptions);
            return Results.Json(entityWithLinks, jsonOptions);
        }

        return Results.Json(state.ApiModel, jsonOptions);
    }

    private static (IReadOnlyList<SelectedField> Fields, IResult? Error) ParseSelectedFields<TApiModel, TKey>(
        RestLibEndpointConfiguration<TApiModel, TKey> config,
        HttpContext httpContext,
        Responses.ProblemDetailsResponder problems)
        where TApiModel : class
        where TKey : notnull
    {
        if (!config.HasFieldSelection)
        {
            return ([], null);
        }

        var rawFields = httpContext.Request.Query["fields"].FirstOrDefault();
        if (string.IsNullOrEmpty(rawFields))
        {
            return ([], null);
        }

        var fieldsResult = FieldSelectionParser.Parse(rawFields, config.FieldSelectionConfiguration);
        return fieldsResult.IsValid
            ? (fieldsResult.Fields, null)
            : ([], problems.Create(Responses.ProblemDetailsFactory.InvalidFields(
                fieldsResult.Errors,
                httpContext.Request.Path)));
    }

    private static Dictionary<string, HateoasLink> BuildLinks<TApiModel, TKey>(
        TApiModel apiModel,
        TKey id,
        RestLibEndpointConfiguration<TApiModel, TKey> config,
        HttpContext httpContext)
        where TApiModel : class
        where TKey : notnull
    {
        var collectionPath = HateoasLinkBuilder.GetCollectionPath(
            httpContext.Request.Path,
            isCollectionEndpoint: false,
            config.KeyRouteParts.Count);
        var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TApiModel, TKey>>();
        var customLinks = customLinksProvider?.GetLinks(apiModel, id);
        return HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, id, config, customLinks);
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

    private static bool ShouldUseProjectionPushdown<TApiModel, TKey>(
        RestLibOptions options,
        RestLibEndpointConfiguration<TApiModel, TKey> config)
        where TApiModel : class
        where TKey : notnull
    {
        return !options.EnableHateoas &&
            !options.EnableETagSupport &&
            config.Hooks is null;
    }
}
