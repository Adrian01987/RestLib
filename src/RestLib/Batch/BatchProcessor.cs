using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Responses;

namespace RestLib.Batch;

/// <summary>
/// Orchestrates the processing of batch requests by delegating per-item
/// validation to <see cref="BatchActionValidator"/> and persistence to
/// <see cref="BatchActionExecutor"/>. When an
/// <see cref="IBatchRepository{TEntity, TKey}"/> is available, bulk
/// methods are used; otherwise operations fall back to one-at-a-time calls.
/// </summary>
internal static class BatchProcessor
{
    /// <summary>
    /// Processes a batch create request.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="itemsElement">The raw JSON items array.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="batchRepository">The optional batch-optimized repository.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="options">The global RestLib options.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The batch response with per-item results.</returns>
    internal static async Task<BatchResponse> ProcessCreateAsync<TEntity, TKey>(
        JsonElement itemsElement,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        IBatchRepository<TEntity, TKey>? batchRepository,
        HookPipeline<TEntity, TKey>? pipeline,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
    {
        var items = BatchActionValidator.DeserializeArray<TEntity>(itemsElement, jsonOptions);
        if (items is null)
        {
            return BatchActionValidator.SingleErrorResponse(0, StatusCodes.Status400BadRequest,
                ProblemDetailsFactory.BadRequest(
                    "The 'items' array could not be deserialized.",
                    httpContext.Request.Path));
        }

        var results = new BatchItemResult?[items.Count];
        var validItems = new List<(int Index, TEntity Entity)>();

        for (var i = 0; i < items.Count; i++)
        {
            var (error, entity) = await BatchActionValidator.ValidateCreateItemAsync<TEntity, TKey>(
                i, items[i], httpContext, pipeline, options, jsonOptions);

            if (error is not null)
            {
                results[i] = error;
                continue;
            }

            validItems.Add((i, entity!));
        }

        await BatchActionExecutor.ExecuteCreatesAsync(
            validItems, results, httpContext, repository, batchRepository, pipeline, options, ct);

        return new BatchResponse { Items = results.ToList()! };
    }

    /// <summary>
    /// Processes a batch update request (full replacement).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="itemsElement">The raw JSON items array.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="batchRepository">The optional batch-optimized repository.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="options">The global RestLib options.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The batch response with per-item results.</returns>
    internal static async Task<BatchResponse> ProcessUpdateAsync<TEntity, TKey>(
        JsonElement itemsElement,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        IBatchRepository<TEntity, TKey>? batchRepository,
        HookPipeline<TEntity, TKey>? pipeline,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
    {
        var items = BatchActionValidator.DeserializeArray<BatchUpdateItem<TKey>>(itemsElement, jsonOptions);
        if (items is null)
        {
            return BatchActionValidator.SingleErrorResponse(0, StatusCodes.Status400BadRequest,
                ProblemDetailsFactory.BadRequest(
                    "The 'items' array could not be deserialized.",
                    httpContext.Request.Path));
        }

        var results = new BatchItemResult?[items.Count];
        var validItems = new List<(int Index, TKey Id, TEntity Entity)>();

        for (var i = 0; i < items.Count; i++)
        {
            var (error, id, entity) = await BatchActionValidator.ValidateUpdateItemAsync<TEntity, TKey>(
                i, items[i], httpContext, repository, pipeline, options, jsonOptions, ct);

            if (error is not null)
            {
                results[i] = error;
                continue;
            }

            validItems.Add((i, id!, entity!));
        }

        await BatchActionExecutor.ExecuteUpdatesAsync(
            validItems, results, httpContext, repository, batchRepository, pipeline, options, ct);

        return new BatchResponse { Items = results.ToList()! };
    }

    /// <summary>
    /// Processes a batch patch request (partial update).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="itemsElement">The raw JSON items array.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="batchRepository">The optional batch-optimized repository.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="options">The global RestLib options.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The batch response with per-item results.</returns>
    internal static async Task<BatchResponse> ProcessPatchAsync<TEntity, TKey>(
        JsonElement itemsElement,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        IBatchRepository<TEntity, TKey>? batchRepository,
        HookPipeline<TEntity, TKey>? pipeline,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
    {
        var items = BatchActionValidator.DeserializeArray<BatchUpdateItem<TKey>>(itemsElement, jsonOptions);
        if (items is null)
        {
            return BatchActionValidator.SingleErrorResponse(0, StatusCodes.Status400BadRequest,
                ProblemDetailsFactory.BadRequest(
                    "The 'items' array could not be deserialized.",
                    httpContext.Request.Path));
        }

        var results = new BatchItemResult?[items.Count];
        var validItems = new List<(int Index, TKey Id, JsonElement Body)>();

        for (var i = 0; i < items.Count; i++)
        {
            var (error, id, body) = await BatchActionValidator.ValidatePatchItemAsync<TEntity, TKey>(
                i, items[i], httpContext, repository, pipeline, ct);

            if (error is not null)
            {
                results[i] = error;
                continue;
            }

            validItems.Add((i, id!, body));
        }

        await BatchActionExecutor.ExecutePatchesAsync(
            validItems, results, httpContext, repository, batchRepository, pipeline, options, jsonOptions, ct);

        return new BatchResponse { Items = results.ToList()! };
    }

    /// <summary>
    /// Processes a batch delete request.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="itemsElement">The raw JSON items array.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="batchRepository">The optional batch-optimized repository.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="options">The global RestLib options.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The batch response with per-item results.</returns>
    internal static async Task<BatchResponse> ProcessDeleteAsync<TEntity, TKey>(
        JsonElement itemsElement,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        IBatchRepository<TEntity, TKey>? batchRepository,
        HookPipeline<TEntity, TKey>? pipeline,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
    {
        var keys = BatchActionValidator.DeserializeArray<TKey>(itemsElement, jsonOptions);
        if (keys is null)
        {
            return BatchActionValidator.SingleErrorResponse(0, StatusCodes.Status400BadRequest,
                ProblemDetailsFactory.BadRequest(
                    "The 'items' array could not be deserialized as a list of IDs.",
                    httpContext.Request.Path));
        }

        var results = new BatchItemResult?[keys.Count];
        var validKeys = new List<(int Index, TKey Key)>();

        for (var i = 0; i < keys.Count; i++)
        {
            var (error, key) = await BatchActionValidator.ValidateDeleteItemAsync<TEntity, TKey>(
                i, keys[i], httpContext, pipeline);

            if (error is not null)
            {
                results[i] = error;
                continue;
            }

            validKeys.Add((i, key!));
        }

        await BatchActionExecutor.ExecuteDeletesAsync(
            validKeys, results, httpContext, repository, batchRepository, pipeline, options, ct);

        return new BatchResponse { Items = results.ToList()! };
    }
}
