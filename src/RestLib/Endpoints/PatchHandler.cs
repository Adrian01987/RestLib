using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hypermedia;
using RestLib.Logging;

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

            RestLibLogMessages.PatchRequestReceived(logger, entityName, id!.ToString()!);

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.Patch, id, logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;
            TEntity? originalEntity = null;

            try
            {
                // OnRequestValidated hook
                var onValidatedResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
                if (onValidatedResult is not null) return onValidatedResult;

                // Check for ETag precondition (If-Match header)
                var (etagEntity, etagError) = await ETagHelper.CheckIfMatchPreconditionAsync(
                    httpContext, repository, id, entityName, options, jsonOptions, ct, logger);
                if (etagError is not null) return etagError;
                if (etagEntity is not null) originalEntity = etagEntity;

                // Fetch original entity if not already fetched (needed for hooks or pre-persist validation)
                if (originalEntity is null && (pipeline is not null || options.EnableValidation))
                {
                    originalEntity = await repository.GetByIdAsync(id, ct);
                    if (originalEntity is null && options.EnableValidation)
                    {
                        return Responses.ProblemDetailsResult.NotFound(
                            entityName,
                            id!,
                            httpContext.Request.Path,
                            jsonOptions,
                            logger);
                    }
                }

                // Validate merged entity BEFORE persisting to prevent invalid data in the repository
                if (options.EnableValidation && originalEntity is not null)
                {
                    var preview = PatchHelper.PreviewPatch(originalEntity, patchDocument, jsonOptions);
                    if (preview is not null)
                    {
                        var validationResult = Validation.EntityValidator.Validate(preview, options.JsonNamingPolicy);
                        if (!validationResult.IsValid)
                        {
                            return Responses.ProblemDetailsResult.ValidationFailed(
                                validationResult.Errors,
                                httpContext.Request.Path,
                                jsonOptions,
                                logger);
                        }
                    }
                }

                // BeforePersist hook — update existing context with original entity
                if (hookContext is not null)
                {
                    hookContext.Entity = originalEntity;
                    hookContext.SetOriginalEntity(originalEntity);
                }

                var beforePersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforePersistAsync);
                if (beforePersistResult is not null) return beforePersistResult;

                var patched = await repository.PatchAsync(id, patchDocument, ct);

                if (patched is null)
                {
                    return Responses.ProblemDetailsResult.NotFound(
                        entityName,
                        id!,
                        httpContext.Request.Path,
                        jsonOptions,
                        logger);
                }

                // AfterPersist hook
                if (hookContext is not null) hookContext.Entity = patched;
                var afterPersistResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteAfterPersistAsync);
                if (afterPersistResult is not null) return afterPersistResult;

                // Add ETag header when enabled
                if (options.EnableETagSupport)
                {
                    var etagGenerator = ETagHelper.ResolveETagGenerator(httpContext);
                    httpContext.Response.Headers.ETag = etagGenerator.Generate(patched);
                }

                // BeforeResponse hook
                var beforeResponseResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseResult is not null) return beforeResponseResult;

                // Inject HATEOAS links into patched entity response
                if (options.EnableHateoas)
                {
                    var collectionPath = HateoasLinkBuilder.GetCollectionPath(httpContext.Request.Path, isCollectionEndpoint: false);
                    var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TEntity, TKey>>();
                    var customLinks = customLinksProvider?.GetLinks(patched, id);
                    var links = HateoasLinkBuilder.BuildEntityLinks(httpContext.Request, collectionPath, id, config, customLinks);
                    var entityWithLinks = HateoasHelper.EntityWithLinks<TEntity, TKey>(patched, links, jsonOptions);
                    return Results.Json(entityWithLinks, jsonOptions);
                }

                return Results.Json(patched, jsonOptions);
            }
            catch (Exception ex) when (IsPatchValidationException(ex))
            {
                return Responses.ProblemDetailsResult.BadRequest(
                    ex.Message,
                    httpContext.Request.Path,
                    jsonOptions,
                    logger);
            }
            catch (Exception ex)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.Patch), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.Patch, ex, id, originalEntity, logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    private static bool IsPatchValidationException(Exception exception)
    {
        return exception.GetType().FullName == "RestLib.EntityFrameworkCore.EfCorePatchValidationException";
    }
}
