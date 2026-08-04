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
                var validatedStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, entity, p => p.ExecuteOnRequestValidatedAsync);
                if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;
                entity = validatedStage.Entity;

                // BeforePersist hook
                var beforePersistStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, entity, p => p.ExecuteBeforePersistAsync);
                if (beforePersistStage.EarlyResult is not null) return beforePersistStage.EarlyResult;
                entity = beforePersistStage.Entity;

                var created = await repository.CreateAsync(entity, ct);

                // The repository-generated identity remains authoritative for response replacements.
                var createdId = EntityKeyHelper.GetEntityKey(created, config.KeySelector);

                // AfterPersist hook
                var afterPersistStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, created, p => p.ExecuteAfterPersistAsync);
                if (afterPersistStage.EarlyResult is not null) return afterPersistStage.EarlyResult;
                created = afterPersistStage.Entity;
                if (createdId is not null)
                {
                    _ = EntityKeyHelper.TrySetEntityKeyParts(created, createdId, config.KeyRouteParts);
                }

                // Extract ID from created entity and set Location header
                var location = $"{httpContext.Request.Path}{EntityKeyHelper.FormatKeyPath(createdId!, config.KeyRouteParts)}";
                httpContext.Response.Headers.Location = location;

                RestLibLogMessages.EntityCreated(logger, createdId?.ToString() ?? string.Empty, location);

                // BeforeResponse hook
                var beforeResponseStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, created, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseStage.EarlyResult is not null) return beforeResponseStage.EarlyResult;
                created = beforeResponseStage.Entity;
                if (createdId is not null)
                {
                    _ = EntityKeyHelper.TrySetEntityKeyParts(created, createdId, config.KeyRouteParts);
                }

                // Generate the ETag from the final response representation.
                if (options.EnableETagSupport)
                {
                    var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
                    httpContext.Response.Headers.ETag = etagGenerator.Generate(created);
                }

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
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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

        var validatedHookEntity = typeof(THookModel) == typeof(TDbModel)
            ? (THookModel)(object)dbEntity
            : (THookModel)(object)apiEntity;
        var validatedStage = await HookHelper.RunEntityHookStageAsync(
            pipeline, hookContext, validatedHookEntity, p => p.ExecuteOnRequestValidatedAsync);
        if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;

        if (typeof(THookModel) == typeof(TDbModel))
        {
            dbEntity = (TDbModel)(object)validatedStage.Entity;
            apiEntity = mapper.ToApi(dbEntity);
        }
        else
        {
            apiEntity = (TApiModel)(object)validatedStage.Entity;
            dbEntity = mapper.ToDb(apiEntity);
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

        var beforePersistHookEntity = typeof(THookModel) == typeof(TDbModel)
            ? (THookModel)(object)dbEntity
            : (THookModel)(object)apiEntity;
        var beforePersistStage = await HookHelper.RunEntityHookStageAsync(
            pipeline, hookContext, beforePersistHookEntity, p => p.ExecuteBeforePersistAsync);
        if (beforePersistStage.EarlyResult is not null) return beforePersistStage.EarlyResult;

        if (typeof(THookModel) == typeof(TDbModel))
        {
            dbEntity = (TDbModel)(object)beforePersistStage.Entity;
        }
        else
        {
            apiEntity = (TApiModel)(object)beforePersistStage.Entity;
            dbEntity = mapper.ToDb(apiEntity);
        }

        var createdDb = await repository.CreateAsync(dbEntity, ct);
        var createdApi = mapper.ToApi(createdDb);
        var createdId = EntityKeyHelper.GetEntityKey(createdApi, config.KeySelector);

        var afterPersistHookEntity = typeof(THookModel) == typeof(TDbModel)
            ? (THookModel)(object)createdDb
            : (THookModel)(object)createdApi;
        var afterPersistStage = await HookHelper.RunEntityHookStageAsync(
            pipeline, hookContext, afterPersistHookEntity, p => p.ExecuteAfterPersistAsync);
        if (afterPersistStage.EarlyResult is not null) return afterPersistStage.EarlyResult;

        if (typeof(THookModel) == typeof(TDbModel))
        {
            createdDb = (TDbModel)(object)afterPersistStage.Entity;
            if (createdId is not null)
            {
                _ = EntityKeyHelper.TrySetEntityKeyParts(createdDb, createdId, config.KeyRouteParts);
            }

            createdApi = mapper.ToApi(createdDb);
        }
        else
        {
            createdApi = (TApiModel)(object)afterPersistStage.Entity;
        }

        if (createdId is not null)
        {
            _ = EntityKeyHelper.TrySetEntityKeyParts(createdApi, createdId, config.KeyRouteParts);
        }

        var location = $"{httpContext.Request.Path}{EntityKeyHelper.FormatKeyPath(createdId!, config.KeyRouteParts)}";
        httpContext.Response.Headers.Location = location;

        RestLibLogMessages.EntityCreated(logger, createdId?.ToString() ?? string.Empty, location);

        var beforeResponseHookEntity = typeof(THookModel) == typeof(TDbModel)
            ? (THookModel)(object)createdDb
            : (THookModel)(object)createdApi;
        var beforeResponseStage = await HookHelper.RunEntityHookStageAsync(
            pipeline, hookContext, beforeResponseHookEntity, p => p.ExecuteBeforeResponseAsync);
        if (beforeResponseStage.EarlyResult is not null) return beforeResponseStage.EarlyResult;

        if (typeof(THookModel) == typeof(TDbModel))
        {
            createdDb = (TDbModel)(object)beforeResponseStage.Entity;
            if (createdId is not null)
            {
                _ = EntityKeyHelper.TrySetEntityKeyParts(createdDb, createdId, config.KeyRouteParts);
            }

            createdApi = mapper.ToApi(createdDb);
        }
        else
        {
            createdApi = (TApiModel)(object)beforeResponseStage.Entity;
        }

        if (createdId is not null)
        {
            _ = EntityKeyHelper.TrySetEntityKeyParts(createdApi, createdId, config.KeyRouteParts);
        }

        if (options.EnableETagSupport)
        {
            var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
            httpContext.Response.Headers.ETag = etagGenerator.Generate(createdApi);
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
