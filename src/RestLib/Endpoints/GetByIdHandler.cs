using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

            RestLibLogMessages.GetByIdRequestReceived(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.GetById, id, logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;

            try
            {
                // Parse and validate field selection before hitting the database
                IReadOnlyList<SelectedField> selectedFields = [];
                if (config.HasFieldSelection)
                {
                    var rawFields = httpContext.Request.Query["fields"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(rawFields))
                    {
                        var fieldsResult = FieldSelectionParser.Parse(rawFields, config.FieldSelectionConfiguration);
                        if (!fieldsResult.IsValid)
                        {
                            return Responses.ProblemDetailsResult.InvalidFields(
                                fieldsResult.Errors,
                                httpContext.Request.Path,
                                jsonOptions,
                                logger,
                                options);
                        }

                        selectedFields = fieldsResult.Fields;
                    }
                }

                TEntity? entity;
                if (selectedFields.Count > 0 &&
                    ShouldUseProjectionPushdown(options, config) &&
                    repository is IFieldSelectionProjectionRepository<TEntity, TKey> projectionRepository)
                {
                    entity = await projectionRepository.GetByIdProjectedAsync(id, selectedFields, ct: ct)
                        ?? await repository.GetByIdAsync(id, ct);
                }
                else
                {
                    entity = await repository.GetByIdAsync(id, ct);
                }

                if (entity is null)
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

                // OnRequestValidated hook
                var validatedStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, entity, p => p.ExecuteOnRequestValidatedAsync);
                if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;
                entity = validatedStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(entity, id, config.KeyRouteParts);

                // BeforeResponse hook
                var beforeResponseStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, entity, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseStage.EarlyResult is not null) return beforeResponseStage.EarlyResult;
                entity = beforeResponseStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(entity, id, config.KeyRouteParts);

                // Conditional requests use the final response representation, including hook replacements.
                if (options.EnableETagSupport)
                {
                    var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
                    var etag = etagGenerator.Generate(entity);

                    var ifNoneMatch = httpContext.Request.Headers.IfNoneMatch;
                    if (!ETagComparer.IfNoneMatchSucceeds(ifNoneMatch, etag))
                    {
                        RestLibLogMessages.GetByIdNotModified(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));
                        httpContext.Response.Headers.ETag = etag;
                        return Results.StatusCode(StatusCodes.Status304NotModified);
                    }

                    httpContext.Response.Headers.ETag = etag;
                }

                // Apply field selection projection if requested
                if (selectedFields.Count > 0)
                {
                    var projected = FieldProjector.Project(
                        entity,
                        selectedFields,
                        jsonOptions,
                        config.FieldSelectionConfiguration.ResponseShape);
                    if (projected is not null)
                    {
                        // Inject HATEOAS links into projected dictionary
                        if (options.EnableHateoas)
                        {
                            var collectionPath = HateoasLinkBuilder.GetCollectionPath(httpContext.Request.Path, isCollectionEndpoint: false, config.KeyRouteParts.Count);
                            var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TEntity, TKey>>();
                            var customLinks = customLinksProvider?.GetLinks(entity, id);
                            var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, id, config, customLinks);
                            HateoasHelper.InjectLinksIntoProjected(projected, links, jsonOptions);
                        }

                        return Results.Json(projected, jsonOptions);
                    }
                }

                // Inject HATEOAS links into full entity response
                if (options.EnableHateoas)
                {
                    var collectionPath = HateoasLinkBuilder.GetCollectionPath(httpContext.Request.Path, isCollectionEndpoint: false, config.KeyRouteParts.Count);
                    var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TEntity, TKey>>();
                    var customLinks = customLinksProvider?.GetLinks(entity, id);
                    var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, id, config, customLinks);
                    var entityWithLinks = HateoasHelper.EntityWithLinks<TEntity, TKey>(entity, links, jsonOptions);
                    return Results.Json(entityWithLinks, jsonOptions);
                }

                return Results.Json(entity, jsonOptions);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.GetById), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.GetById, ex, id, logger: logger);
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

            RestLibLogMessages.GetByIdRequestReceived(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

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
                    return await ExecuteMappedGetByIdAsync<TApiModel, TDbModel, TDbModel, TKey>(
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
                return await ExecuteMappedGetByIdAsync<TApiModel, TDbModel, TApiModel, TKey>(
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

    private static async Task<IResult> ExecuteMappedGetByIdAsync<TApiModel, TDbModel, THookModel, TKey>(
        TKey id,
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
        IReadOnlyList<SelectedField> selectedFields = [];
        if (config.HasFieldSelection)
        {
            var rawFields = httpContext.Request.Query["fields"].FirstOrDefault();
            if (!string.IsNullOrEmpty(rawFields))
            {
                var fieldsResult = FieldSelectionParser.Parse(rawFields, config.FieldSelectionConfiguration);
                if (!fieldsResult.IsValid)
                {
                    return Responses.ProblemDetailsResult.InvalidFields(
                        fieldsResult.Errors,
                        httpContext.Request.Path,
                        jsonOptions,
                        logger,
                        options);
                }

                selectedFields = fieldsResult.Fields;
            }
        }

        var dbEntity = await repository.GetByIdAsync(id, ct);
        if (dbEntity is null)
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

        var apiEntity = mapper.ToApi(dbEntity);

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                hookContext.Entity = (THookModel)(object)dbEntity;
            }
            else
            {
                hookContext.Entity = (THookModel)(object)apiEntity;
            }
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
                _ = EntityKeyHelper.TrySetEntityKeyParts(apiEntity, id, config.KeyRouteParts);
            }
        }
        _ = EntityKeyHelper.TrySetEntityKeyParts(apiEntity, id, config.KeyRouteParts);

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                hookContext.Entity = (THookModel)(object)dbEntity;
            }
            else
            {
                hookContext.Entity = (THookModel)(object)apiEntity;
            }
        }

        var beforeResponseResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
        if (beforeResponseResult is not null) return beforeResponseResult;

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
                _ = EntityKeyHelper.TrySetEntityKeyParts(apiEntity, id, config.KeyRouteParts);
            }
        }
        _ = EntityKeyHelper.TrySetEntityKeyParts(apiEntity, id, config.KeyRouteParts);

        if (options.EnableETagSupport)
        {
            var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
            var etag = etagGenerator.Generate(apiEntity);

            var ifNoneMatch = httpContext.Request.Headers.IfNoneMatch;
            if (!ETagComparer.IfNoneMatchSucceeds(ifNoneMatch, etag))
            {
                RestLibLogMessages.GetByIdNotModified(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));
                httpContext.Response.Headers.ETag = etag;
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            httpContext.Response.Headers.ETag = etag;
        }

        if (selectedFields.Count > 0)
        {
            var projected = FieldProjector.Project(
                apiEntity,
                selectedFields,
                jsonOptions,
                config.FieldSelectionConfiguration.ResponseShape);
            if (projected is not null)
            {
                if (options.EnableHateoas)
                {
                    var collectionPath = HateoasLinkBuilder.GetCollectionPath(httpContext.Request.Path, isCollectionEndpoint: false, config.KeyRouteParts.Count);
                    var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TApiModel, TKey>>();
                    var customLinks = customLinksProvider?.GetLinks(apiEntity, id);
                    var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, id, config, customLinks);
                    HateoasHelper.InjectLinksIntoProjected(projected, links, jsonOptions);
                }

                return Results.Json(projected, jsonOptions);
            }
        }

        if (options.EnableHateoas)
        {
            var collectionPath = HateoasLinkBuilder.GetCollectionPath(httpContext.Request.Path, isCollectionEndpoint: false, config.KeyRouteParts.Count);
            var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TApiModel, TKey>>();
            var customLinks = customLinksProvider?.GetLinks(apiEntity, id);
            var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, id, config, customLinks);
            var entityWithLinks = HateoasHelper.EntityWithLinks<TApiModel, TKey>(apiEntity, links, jsonOptions);
            return Results.Json(entityWithLinks, jsonOptions);
        }

        return Results.Json(apiEntity, jsonOptions);
    }

    private static bool ShouldUseProjectionPushdown<TEntity, TKey>(
        RestLibOptions options,
        RestLibEndpointConfiguration<TEntity, TKey> config)
        where TEntity : class
        where TKey : notnull
    {
        // The EF Core projection-capability path also handles nested field
        // selections by loading required navigations before falling back.
        return !options.EnableHateoas &&
            !options.EnableETagSupport &&
            config.Hooks is null;
    }
}
