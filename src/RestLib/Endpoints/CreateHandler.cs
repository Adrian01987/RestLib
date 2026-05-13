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

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.Create, entity: entity, logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;
            // Entity might have been modified by hook
            if (hookContext is not null) entity = hookContext.Entity ?? entity;

            try
            {
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
                var location = $"{httpContext.Request.Path}{EntityKeyHelper.FormatKeyPath(createdId!, config.KeyRouteParts)}";
                httpContext.Response.Headers.Location = location;

                RestLibLogMessages.EntityCreated(logger, createdId?.ToString() ?? string.Empty, location);

                // Add ETag header when enabled
                if (options.EnableETagSupport)
                {
                    var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
                    httpContext.Response.Headers.ETag = etagGenerator.Generate(created);
                }

                // BeforeResponse hook
                var beforeResponseResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseResult is not null) return beforeResponseResult;

                // Inject HATEOAS links into created entity response
                if (options.EnableHateoas && createdId is not null)
                {
                    var collectionPath = httpContext.Request.Path.ToString();
                    var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TEntity, TKey>>();
                    var customLinks = customLinksProvider?.GetLinks(created, createdId);
                    var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, createdId, config, customLinks);
                    var entityWithLinks = HateoasHelper.EntityWithLinks<TEntity, TKey>(created, links, jsonOptions);
                    return Results.Json(entityWithLinks, jsonOptions, statusCode: StatusCodes.Status201Created);
                }

                return Results.Json(created, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (Exception ex)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Create), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.Create, ex, entity: entity, logger: logger);
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

            RestLibLogMessages.CreateRequestReceived(logger);

            if (config.UsesDbModelHooks)
            {
                var dbEntity = mapper.ToDb(apiEntity);
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
                    apiEntity = mapper.ToApi(dbEntity);
                }

                try
                {
                    return await ExecuteMappedCreateAsync<TApiModel, TDbModel, TDbModel, TKey>(
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
                        pipeline,
                        hookContext);
                }
                catch (Exception ex)
                {
                    RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Create), ex);
                    var errorResult = await HookHelper.HandleErrorHookAsync(
                        pipeline,
                        httpContext,
                        RestLibOperation.Create,
                        ex,
                        entity: dbEntity,
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

            try
            {
                return await ExecuteMappedCreateAsync<TApiModel, TDbModel, TApiModel, TKey>(
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
                    apiPipeline,
                    apiHookContext);
            }
            catch (Exception ex)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Create), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(
                    apiPipeline,
                    httpContext,
                    RestLibOperation.Create,
                    ex,
                    entity: apiEntity,
                    logger: logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    private static async Task<IResult> ExecuteMappedCreateAsync<TApiModel, TDbModel, THookModel, TKey>(
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
        HookPipeline<THookModel, TKey>? pipeline,
        HookContext<THookModel, TKey>? hookContext)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class
        where TKey : notnull
    {
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
                apiEntity = mapper.ToApi(dbEntity);
            }
            else
            {
                apiEntity = (TApiModel)(object)(hookContext.Entity ?? (THookModel)(object)apiEntity);
                dbEntity = mapper.ToDb(apiEntity);
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

        if (hookContext is not null)
        {
            hookContext.Entity = typeof(THookModel) == typeof(TDbModel)
                ? (THookModel)(object)dbEntity
                : (THookModel)(object)apiEntity;
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

        var createdDb = await repository.CreateAsync(dbEntity, ct);
        var createdApi = mapper.ToApi(createdDb);

        if (hookContext is not null)
        {
            hookContext.Entity = typeof(THookModel) == typeof(TDbModel)
                ? (THookModel)(object)createdDb
                : (THookModel)(object)createdApi;
        }

        var afterPersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteAfterPersistAsync);
        if (afterPersistResult is not null) return afterPersistResult;

        if (hookContext is not null && typeof(THookModel) == typeof(TDbModel))
        {
            createdDb = (TDbModel)(object)(hookContext.Entity ?? (THookModel)(object)createdDb);
            createdApi = mapper.ToApi(createdDb);
        }

        var createdId = EntityKeyHelper.GetEntityKey(createdApi, config.KeySelector);
        var location = $"{httpContext.Request.Path}{EntityKeyHelper.FormatKeyPath(createdId!, config.KeyRouteParts)}";
        httpContext.Response.Headers.Location = location;

        RestLibLogMessages.EntityCreated(logger, createdId?.ToString() ?? string.Empty, location);

        if (options.EnableETagSupport)
        {
            var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
            httpContext.Response.Headers.ETag = etagGenerator.Generate(createdApi);
        }

        if (hookContext is not null)
        {
            hookContext.Entity = typeof(THookModel) == typeof(TDbModel)
                ? (THookModel)(object)createdDb
                : (THookModel)(object)createdApi;
        }

        var beforeResponseResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
        if (beforeResponseResult is not null) return beforeResponseResult;

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                createdDb = (TDbModel)(object)(hookContext.Entity ?? (THookModel)(object)createdDb);
                createdApi = mapper.ToApi(createdDb);
            }
            else
            {
                createdApi = (TApiModel)(object)(hookContext.Entity ?? (THookModel)(object)createdApi);
            }
        }

        if (options.EnableHateoas && createdId is not null)
        {
            var collectionPath = httpContext.Request.Path.ToString();
            var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TApiModel, TKey>>();
            var customLinks = customLinksProvider?.GetLinks(createdApi, createdId);
            var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, createdId, config, customLinks);
            var entityWithLinks = HateoasHelper.EntityWithLinks<TApiModel, TKey>(createdApi, links, jsonOptions);
            return Results.Json(entityWithLinks, jsonOptions, statusCode: StatusCodes.Status201Created);
        }

        return Results.Json(createdApi, jsonOptions, statusCode: StatusCodes.Status201Created);
    }
}
