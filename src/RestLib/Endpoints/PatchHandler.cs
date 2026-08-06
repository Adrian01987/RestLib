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
/// Handles PATCH requests for partial entity updates (JSON Merge Patch - RFC 7396).
/// </summary>
internal static class PatchHandler
{
    /// <summary>
    /// Creates the delegate for the Patch endpoint.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="entityName">The clean entity type name used in error messages (e.g., "Product").</param>
    /// <returns>The request delegate.</returns>
    internal static Func<TKey, JsonElement, IRepository<TEntity, TKey>, HttpContext, CancellationToken, Task<IResult>>
        CreateDelegate<TEntity, TKey>(
            RestLibEndpointConfiguration<TEntity, TKey> config,
            string entityName)
        where TEntity : class
        where TKey : notnull
    {
        return async (
            TKey id,
            JsonElement patchDocument,
            IRepository<TEntity, TKey> repository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Patch");
            var problems = Responses.ProblemDetailsResult.CreateResponder(jsonOptions, logger, options);

            if (PatchHelper.TryGetPatchedKeyProperty<TEntity, TKey>(
                patchDocument,
                config.KeyRouteParts,
                jsonOptions,
                out var patchedKeyProperty))
            {
                return problems.Create(Responses.ProblemDetailsFactory.BadRequest(
                    PatchHelper.KeyModificationError(patchedKeyProperty!),
                    httpContext.Request.Path));
            }

            RestLibLogMessages.PatchRequestReceived(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.Patch, id, logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;
            TEntity? originalEntity = null;

            try
            {
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

                // Fetch original entity if not already fetched. PATCH preview uses the
                // current entity as the merge base so malformed patch documents can be
                // rejected before repository persistence runs.
                if (originalEntity is null)
                {
                    originalEntity = await repository.GetByIdAsync(id, ct);
                    if (originalEntity is null)
                    {
                        return problems.Create(Responses.ProblemDetailsFactory.NotFound(
                            entityName,
                            id!,
                            config.KeyRouteParts,
                            httpContext.Request.Path));
                    }
                }

                var preview = PatchHelper.PreviewPatch(originalEntity, patchDocument, jsonOptions, logger);
                if (preview is null)
                {
                    return problems.Create(Responses.ProblemDetailsFactory.BadRequest(
                        "The patch document could not be applied to the resource.",
                        httpContext.Request.Path));
                }

                // Validate merged entity BEFORE persisting to prevent invalid data in the repository
                if (options.EnableValidation)
                {
                    var validationResult = RestLibResourceValidator.Validate(
                        preview,
                        config,
                        jsonOptions.PropertyNamingPolicy);
                    if (!validationResult.IsValid)
                    {
                        return problems.Create(Responses.ProblemDetailsFactory.ValidationFailed(
                            validationResult.Errors,
                            httpContext.Request.Path));
                    }
                }

                if (hookContext is not null) hookContext.SetOriginalEntity(originalEntity);

                // OnRequestValidated receives the validated merge preview as its effective entity.
                var validatedStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, preview, p => p.ExecuteOnRequestValidatedAsync);
                if (validatedStage.EarlyResult is not null) return validatedStage.EarlyResult;
                preview = validatedStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(preview, id, config.KeyRouteParts);

                // A replacement can introduce new invalid state, so validate the effective entity.
                if (options.EnableValidation)
                {
                    var validationResult = RestLibResourceValidator.Validate(
                        preview,
                        config,
                        jsonOptions.PropertyNamingPolicy);
                    if (!validationResult.IsValid)
                    {
                        return problems.Create(Responses.ProblemDetailsFactory.ValidationFailed(
                            validationResult.Errors,
                            httpContext.Request.Path));
                    }
                }

                var beforePersistStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, preview, p => p.ExecuteBeforePersistAsync);
                if (beforePersistStage.EarlyResult is not null) return beforePersistStage.EarlyResult;
                preview = beforePersistStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(preview, id, config.KeyRouteParts);

                TEntity? patched;
                if (ifMatchPrecondition is not null)
                {
                    var conditionalRepository = (IConditionalWriteRepository<TEntity, TKey>)repository;
                    var conditionalResult = pipeline?.HasPrePersistEntityHooks == true
                        ? await conditionalRepository.UpdateConditionallyAsync(id, preview, ifMatchPrecondition, ct)
                        : await conditionalRepository.PatchConditionallyAsync(id, patchDocument, ifMatchPrecondition, ct);
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
                    patched = conditionalResult.Entity!;
                }
                else if (pipeline?.HasPrePersistEntityHooks == true)
                {
                    // PATCH repositories accept only the original document, so an effective entity
                    // selected by hooks must be persisted through the full-update contract.
                    patched = await repository.UpdateAsync(id, preview, ct);
                }
                else
                {
                    patched = await repository.PatchAsync(id, patchDocument, ct);
                }

                if (patched is null)
                {
                    return problems.Create(Responses.ProblemDetailsFactory.NotFound(
                        entityName,
                        id!,
                        config.KeyRouteParts,
                        httpContext.Request.Path));
                }

                // AfterPersist hook
                var afterPersistStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, patched, p => p.ExecuteAfterPersistAsync);
                if (afterPersistStage.EarlyResult is not null) return afterPersistStage.EarlyResult;
                patched = afterPersistStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(patched, id, config.KeyRouteParts);

                // BeforeResponse hook
                var beforeResponseStage = await HookHelper.RunEntityHookStageAsync(
                    pipeline, hookContext, patched, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseStage.EarlyResult is not null) return beforeResponseStage.EarlyResult;
                patched = beforeResponseStage.Entity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(patched, id, config.KeyRouteParts);

                if (options.EnableETagSupport)
                {
                    var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
                    httpContext.Response.Headers.ETag = etagGenerator.Generate(patched);
                }

                // Inject HATEOAS links into patched entity response
                if (options.EnableHateoas)
                {
                    var collectionPath = HateoasLinkBuilder.GetCollectionPath(httpContext.Request.Path, isCollectionEndpoint: false, config.KeyRouteParts.Count);
                    var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TEntity, TKey>>();
                    var customLinks = customLinksProvider?.GetLinks(patched, id);
                    var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, id, config, customLinks);
                    var entityWithLinks = HateoasHelper.EntityWithLinks<TEntity, TKey>(patched, links, jsonOptions);
                    return Results.Json(entityWithLinks, jsonOptions);
                }

                return Results.Json(patched, jsonOptions);
            }
            catch (PatchValidationException ex)
            {
                return problems.Create(Responses.ProblemDetailsFactory.BadRequest(
                    ex.Message,
                    httpContext.Request.Path));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Patch), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.Patch, ex, id, originalEntity, logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    /// <summary>
    /// Creates the delegate for the mapped Patch endpoint.
    /// </summary>
    /// <typeparam name="TApiModel">The API model type.</typeparam>
    /// <typeparam name="TDbModel">The DB model type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="entityName">The API entity name used in error messages.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<TKey, JsonElement, HttpContext, CancellationToken, Task<IResult>>
        CreateMappedDelegate<TApiModel, TDbModel, TKey>(
            RestLibEndpointConfiguration<TApiModel, TDbModel, TKey> config,
            string entityName)
        where TApiModel : class
        where TDbModel : class
        where TKey : notnull
    {
        return async (
            id,
            patchDocument,
            httpContext,
            ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Patch");
            var problems = Responses.ProblemDetailsResult.CreateResponder(jsonOptions, logger, options);
            var repository = httpContext.RequestServices.GetRequiredService<IRepository<TDbModel, TKey>>();
            var mapper = RestLibMapperResolver.Resolve<TApiModel, TDbModel>(
                httpContext.RequestServices,
                config.MapperName,
                config.UseAutoMapper,
                config.ResourceName);

            if (PatchHelper.TryGetPatchedKeyProperty<TApiModel, TKey>(
                patchDocument,
                config.KeyRouteParts,
                jsonOptions,
                out var patchedKeyProperty))
            {
                return problems.Create(Responses.ProblemDetailsFactory.BadRequest(
                    PatchHelper.KeyModificationError(patchedKeyProperty!),
                    httpContext.Request.Path));
            }

            RestLibLogMessages.PatchRequestReceived(logger, entityName, EntityKeyHelper.FormatKeyForDisplay(id, config.KeyRouteParts));

            if (config.UsesDbModelHooks)
            {
                var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TDbModel, TKey>(
                    config.DbModelHooks,
                    httpContext,
                    RestLibOperation.Patch,
                    id,
                    logger: logger);
                if (pipelineEarlyResult is not null) return pipelineEarlyResult;

                try
                {
                    return await ExecuteMappedPatchAsync<TApiModel, TDbModel, TDbModel, TKey>(
                        id,
                        patchDocument,
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
                catch (PatchValidationException ex)
                {
                    return problems.Create(Responses.ProblemDetailsFactory.BadRequest(
                        ex.Message,
                        httpContext.Request.Path));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Patch), ex);
                    var errorResult = await HookHelper.HandleErrorHookAsync(
                        pipeline,
                        httpContext,
                        RestLibOperation.Patch,
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
                RestLibOperation.Patch,
                id,
                logger: logger);
            if (apiPipelineEarlyResult is not null) return apiPipelineEarlyResult;

            try
            {
                return await ExecuteMappedPatchAsync<TApiModel, TDbModel, TApiModel, TKey>(
                    id,
                    patchDocument,
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
            catch (PatchValidationException ex)
            {
                return problems.Create(Responses.ProblemDetailsFactory.BadRequest(
                    ex.Message,
                    httpContext.Request.Path));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Patch), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(
                    apiPipeline,
                    httpContext,
                    RestLibOperation.Patch,
                    ex,
                    id,
                    logger: logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    private static async Task<IResult> ExecuteMappedPatchAsync<TApiModel, TDbModel, THookModel, TKey>(
        TKey id,
        JsonElement patchDocument,
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
        var problems = Responses.ProblemDetailsResult.CreateResponder(jsonOptions, logger, options);
        TDbModel? originalDb = null;
        TApiModel? originalApi = null;

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

        if (originalDb is null && (pipeline is not null || options.EnableValidation))
        {
            originalDb = await repository.GetByIdAsync(id, ct);
            if (originalDb is null && options.EnableValidation)
            {
                return problems.Create(Responses.ProblemDetailsFactory.NotFound(
                    entityName,
                    id!,
                    config.KeyRouteParts,
                    httpContext.Request.Path));
            }

            originalApi = originalDb is not null ? mapper.ToApi(originalDb) : null;
        }

        if (originalApi is null)
        {
            var notFoundCandidate = await repository.GetByIdAsync(id, ct);
            if (notFoundCandidate is null)
            {
                return problems.Create(Responses.ProblemDetailsFactory.NotFound(
                    entityName,
                    id!,
                    config.KeyRouteParts,
                    httpContext.Request.Path));
            }

            originalDb = notFoundCandidate;
            originalApi = mapper.ToApi(notFoundCandidate);
        }

        var patchedApi = PatchHelper.PreviewPatch(originalApi, patchDocument, jsonOptions, logger);
        if (patchedApi is null)
        {
            return problems.Create(Responses.ProblemDetailsFactory.BadRequest(
                "The patch document could not be applied to the resource.",
                httpContext.Request.Path));
        }

        if (options.EnableValidation)
        {
            var validationResult = RestLibResourceValidator.Validate(
                patchedApi,
                config,
                jsonOptions.PropertyNamingPolicy);
            if (!validationResult.IsValid)
            {
                return problems.Create(Responses.ProblemDetailsFactory.ValidationFailed(
                    validationResult.Errors,
                    httpContext.Request.Path));
            }
        }

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                var patchedDb = mapper.ToDb(patchedApi);
                hookContext.Entity = (THookModel)(object)patchedDb;
                hookContext.SetOriginalEntity((THookModel?)(object?)originalDb);
            }
            else
            {
                hookContext.Entity = (THookModel)(object)patchedApi;
                hookContext.SetOriginalEntity((THookModel?)(object?)originalApi);
            }
        }

        var onValidatedResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
        if (onValidatedResult is not null) return onValidatedResult;

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                var validatedDb = (TDbModel)(object)(hookContext.Entity ?? (THookModel)(object)mapper.ToDb(patchedApi));
                patchedApi = mapper.ToApi(validatedDb);
            }
            else
            {
                patchedApi = (TApiModel)(object)(hookContext.Entity ?? (THookModel)(object)patchedApi);
            }
        }

        if (options.EnableValidation)
        {
            var validationResult = RestLibResourceValidator.Validate(
                patchedApi,
                config,
                jsonOptions.PropertyNamingPolicy);
            if (!validationResult.IsValid)
            {
                return problems.Create(Responses.ProblemDetailsFactory.ValidationFailed(
                    validationResult.Errors,
                    httpContext.Request.Path));
            }
        }

        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                var patchedDb = mapper.ToDb(patchedApi);
                hookContext.Entity = (THookModel)(object)patchedDb;
                hookContext.SetOriginalEntity((THookModel?)(object?)originalDb);
            }
            else
            {
                hookContext.Entity = (THookModel)(object)patchedApi;
                hookContext.SetOriginalEntity((THookModel?)(object?)originalApi);
            }
        }

        var beforePersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforePersistAsync);
        if (beforePersistResult is not null) return beforePersistResult;

        TDbModel persistedDb;
        if (hookContext is not null)
        {
            if (typeof(THookModel) == typeof(TDbModel))
            {
                persistedDb = (TDbModel)(object)(hookContext.Entity ?? (THookModel)(object)mapper.ToDb(patchedApi));
                patchedApi = mapper.ToApi(persistedDb);
            }
            else
            {
                patchedApi = (TApiModel)(object)(hookContext.Entity ?? (THookModel)(object)patchedApi);
                persistedDb = mapper.ToDb(patchedApi);
            }
        }
        else
        {
            persistedDb = mapper.ToDb(patchedApi);
        }

        _ = EntityKeyHelper.TrySetEntityKeyParts(persistedDb, id, config.KeyRouteParts);

        TDbModel? updatedDb;
        if (ifMatchPrecondition is null)
        {
            updatedDb = await repository.UpdateAsync(id, persistedDb, ct);
        }
        else
        {
            var conditionalResult = await ((IConditionalWriteRepository<TDbModel, TKey>)repository)
                .UpdateConditionallyAsync(id, persistedDb, ifMatchPrecondition, ct);
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
