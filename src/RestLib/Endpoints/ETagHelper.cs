using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Configuration;
using RestLib.Logging;

namespace RestLib.Endpoints;

/// <summary>
/// Helper methods for ETag generation and If-Match precondition checking.
/// </summary>
internal static class ETagHelper
{
    /// <summary>
    /// Resolves the ETag generator from the service provider.
    /// This method is only called when <see cref="RestLibOptions.EnableETagSupport"/> is <c>true</c>,
    /// which guarantees an <see cref="IETagGenerator"/> singleton was registered by
    /// <see cref="RestLibServiceExtensions.AddRestLib"/>.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The resolved ETag generator.</returns>
    internal static IETagGenerator ResolveETagGenerator(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<IETagGenerator>();

    /// <summary>
    /// Creates an <c>If-Match</c> predicate for an entity when conditional ETag handling applies.
    /// </summary>
    internal static Func<TEntity, bool>? CreateIfMatchPrecondition<TEntity>(
        HttpContext httpContext,
        RestLibOptions options)
        where TEntity : class
    {
        if (!options.EnableETagSupport)
        {
            return null;
        }

        var ifMatchHeader = httpContext.Request.Headers.IfMatch;
        if (StringValues.IsNullOrEmpty(ifMatchHeader))
        {
            return null;
        }

        var etagGenerator = ResolveETagGenerator(httpContext);
        return current => ETagComparer.IfMatchSucceeds(ifMatchHeader, etagGenerator.Generate(current));
    }

    /// <summary>
    /// Creates an <c>If-Match</c> predicate for a mapped resource when conditional ETag handling applies.
    /// </summary>
    internal static Func<TDbModel, bool>? CreateIfMatchPrecondition<TApiModel, TDbModel>(
        HttpContext httpContext,
        RestLibOptions options,
        IRestLibMapper<TApiModel, TDbModel> mapper)
        where TApiModel : class
        where TDbModel : class
    {
        var apiPrecondition = CreateIfMatchPrecondition<TApiModel>(httpContext, options);
        return apiPrecondition is null
            ? null
            : current => apiPrecondition(mapper.ToApi(current));
    }

    /// <summary>
    /// Creates the error returned when an <c>If-Match</c> request targets a repository without
    /// atomic conditional-write support.
    /// </summary>
    internal static IResult ConditionalWriteNotSupported(
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions,
        RestLibOptions options,
        ILogger? logger)
    {
        return Responses.ProblemDetailsResult.ConditionalWriteNotSupported(
            $"The configured repository must implement {nameof(IConditionalWriteRepository<object, object>)} " +
            "to process If-Match safely.",
            httpContext.Request.Path,
            jsonOptions,
            logger,
            options);
    }

    /// <summary>
    /// Converts an unsuccessful conditional repository result into its HTTP error response.
    /// </summary>
    internal static IResult? ToErrorResult<TEntity, TKey>(
        ConditionalWriteResult<TEntity> result,
        HttpContext httpContext,
        TKey id,
        string entityName,
        IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts,
        JsonSerializerOptions jsonOptions,
        RestLibOptions options,
        ILogger? logger)
        where TEntity : class
        where TKey : notnull
    {
        if (result.Status == ConditionalWriteStatus.Succeeded)
        {
            return null;
        }

        if (result.Status == ConditionalWriteStatus.NotFound)
        {
            return Responses.ProblemDetailsResult.NotFound(
                entityName,
                id,
                keyRouteParts,
                httpContext.Request.Path,
                jsonOptions,
                logger,
                options);
        }

        if (logger is not null)
        {
            RestLibLogMessages.ETagPreconditionFailed(logger, entityName, id.ToString()!);
        }

        return Responses.ProblemDetailsResult.PreconditionFailed(
            "The resource has been modified since you last retrieved it.",
            httpContext.Request.Path,
            jsonOptions,
            logger,
            options);
    }

    /// <summary>
    /// Checks the If-Match precondition header when ETag support is enabled.
    /// If the header is present, fetches the current entity, compares ETags,
    /// and returns the appropriate error result if the precondition fails.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The repository to fetch the entity from.</param>
    /// <param name="id">The entity identifier.</param>
    /// <param name="entityName">The clean entity type name used in error messages.</param>
    /// <param name="options">The RestLib options (checked for ETag support).</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <param name="logger">Optional logger for recording precondition failures.</param>
    /// <returns>
    /// A tuple where <c>Entity</c> is the fetched entity (if the If-Match header was present
    /// and the precondition succeeded), and <c>Error</c> is an error result if the precondition
    /// failed (not found or ETag mismatch). Both are <c>null</c> when ETag support is disabled
    /// or no If-Match header is present.
    /// </returns>
    internal static async Task<(TEntity? Entity, IResult? Error)> CheckIfMatchPreconditionAsync<TEntity, TKey>(
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        TKey id,
        string entityName,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct,
        ILogger? logger = null)
        where TEntity : class
        where TKey : notnull
    {
        var precondition = CreateIfMatchPrecondition<TEntity>(httpContext, options);
        if (precondition is null)
        {
            return (null, null);
        }

        // Get current entity to compare ETags
        var current = await repository.GetByIdAsync(id, ct);
        if (current is null)
        {
            var notFoundResult = Responses.ProblemDetailsResult.NotFound(
                entityName,
                id!,
                [new RestLib.Configuration.RestLibKeyRoutePart<TKey>(string.Empty, "id", typeof(TKey), static key => key)],
                httpContext.Request.Path,
                jsonOptions,
                logger: logger,
                options: options);
            return (null, notFoundResult);
        }

        if (!precondition(current))
        {
            if (logger is not null)
            {
                RestLibLogMessages.ETagPreconditionFailed(logger, entityName, id!.ToString()!);
            }

            var preconditionResult = Responses.ProblemDetailsResult.PreconditionFailed(
                "The resource has been modified since you last retrieved it.",
                httpContext.Request.Path,
                jsonOptions,
                logger: logger,
                options: options);
            return (null, preconditionResult);
        }

        return (current, null);
    }

    /// <summary>
    /// Checks the If-Match precondition header for a mapped resource.
    /// The current DB model is mapped to the API model before ETag generation.
    /// </summary>
    /// <typeparam name="TApiModel">The API model type.</typeparam>
    /// <typeparam name="TDbModel">The DB model type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The DB-model repository.</param>
    /// <param name="mapper">The API/DB mapper.</param>
    /// <param name="id">The resource identifier.</param>
    /// <param name="entityName">The API entity name for error messages.</param>
    /// <param name="options">The RestLib options.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>
    /// The fetched DB model and mapped API model when the precondition succeeds,
    /// or an error result if it fails.
    /// </returns>
    internal static async Task<(TDbModel? DbEntity, TApiModel? ApiEntity, IResult? Error)> CheckIfMatchPreconditionAsync<TApiModel, TDbModel, TKey>(
        HttpContext httpContext,
        IRepository<TDbModel, TKey> repository,
        IRestLibMapper<TApiModel, TDbModel> mapper,
        TKey id,
        string entityName,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct,
        ILogger? logger = null)
        where TApiModel : class
        where TDbModel : class
        where TKey : notnull
    {
        var precondition = CreateIfMatchPrecondition<TApiModel, TDbModel>(httpContext, options, mapper);
        if (precondition is null)
        {
            return (null, null, null);
        }

        var currentDb = await repository.GetByIdAsync(id, ct);
        if (currentDb is null)
        {
            var notFoundResult = Responses.ProblemDetailsResult.NotFound(
                entityName,
                id!,
                [new RestLib.Configuration.RestLibKeyRoutePart<TKey>(string.Empty, "id", typeof(TKey), static key => key)],
                httpContext.Request.Path,
                jsonOptions,
                logger: logger,
                options: options);
            return (null, null, notFoundResult);
        }

        var currentApi = mapper.ToApi(currentDb);
        if (!precondition(currentDb))
        {
            if (logger is not null)
            {
                RestLibLogMessages.ETagPreconditionFailed(logger, entityName, id!.ToString()!);
            }

            var preconditionResult = Responses.ProblemDetailsResult.PreconditionFailed(
                "The resource has been modified since you last retrieved it.",
                httpContext.Request.Path,
                jsonOptions,
                logger: logger,
                options: options);
            return (null, null, preconditionResult);
        }

        return (currentDb, currentApi, null);
    }
}
