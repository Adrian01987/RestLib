using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Endpoints;
using RestLib.Logging;
using RestLib.Responses;
using RestLib.Validation;

namespace RestLib.Batch;

/// <summary>
/// Batch patch pipeline. Deserializes <see cref="BatchUpdateItem{TKey}"/> items,
/// validates existence, performs pre-persist validation (preview merge), and persists
/// via <see cref="IRepository{TEntity, TKey}.PatchAsync"/> or
/// <see cref="IBatchRepository{TEntity, TKey}.PatchManyAsync"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class BatchPatchPipeline<TEntity, TKey>
    : BatchActionPipeline<TEntity, TKey, BatchUpdateItem<TKey>, (int Index, TKey Id, JsonElement Body)>
    where TEntity : class
    where TKey : notnull
{
    /// <inheritdoc/>
    protected override int SuccessStatusCode => StatusCodes.Status200OK;

    /// <inheritdoc/>
    protected override RestLibOperation Operation => RestLibOperation.BatchPatch;

    /// <inheritdoc/>
    protected override async Task<(BatchItemResult? Error, (int Index, TKey Id, JsonElement Body) ValidItem)> ValidateItemAsync(
        int index,
        BatchUpdateItem<TKey>? item,
        BatchContext<TEntity, TKey> context)
    {
        if (item is null)
        {
            return (BadRequestResult(index, $"Item at index {index} could not be deserialized.", context.HttpContext.Request.Path), default);
        }

        // Fetch existing entity (needed for 404 check and hook context)
        var existing = await context.Repository.GetByIdAsync(item.Id, context.CancellationToken);
        if (existing is null)
        {
            var entityName = typeof(TEntity).Name;
            return (NotFoundResult(index, entityName, item.Id!, context.HttpContext.Request.Path), default);
        }

        // Hooks: OnRequestReceived, OnRequestValidated, BeforePersist
        if (context.Pipeline is not null)
        {
            var hookContext = context.Pipeline.CreateContext(
                context.HttpContext, RestLibOperation.BatchPatch,
                resourceId: item.Id, entity: existing, originalEntity: existing);

            var hookError = await RunPrePersistHooksAsync(index, context.Pipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, default);
            }
        }

        return (null, (index, item.Id, item.Body));
    }

    /// <inheritdoc/>
    protected override int GetIndex((int Index, TKey Id, JsonElement Body) validItem) => validItem.Index;

    /// <inheritdoc/>
    protected override TKey? GetResourceId((int Index, TKey Id, JsonElement Body) validItem) => validItem.Id;

    /// <inheritdoc/>
    protected override async Task PersistBulkAsync(
        List<(int Index, TKey Id, JsonElement Body)> validItems,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        // Pre-persist validation: bulk-fetch originals, preview merges, validate before persisting.
        // Uses GetByIdsAsync for a single bulk fetch instead of N individual GetByIdAsync calls.
        var itemsToPersist = validItems;
        if (context.Options.EnableValidation)
        {
            var ids = validItems.Select(v => v.Id).ToList();
            var originals = await context.BatchRepository!.GetByIdsAsync(ids, context.CancellationToken);

            itemsToPersist = new List<(int Index, TKey Id, JsonElement Body)>();
            foreach (var (index, id, body) in validItems)
            {
                if (!originals.TryGetValue(id, out var original))
                {
                    var entityName = typeof(TEntity).Name;
                    RestLibLogMessages.BatchPatchItemNotFound(context.Logger, index, entityName, id!);
                    results[index] = new BatchItemResult
                    {
                        Index = index,
                        Status = StatusCodes.Status404NotFound,
                        Error = ProblemDetailsFactory.NotFound(entityName, id!, context.HttpContext.Request.Path)
                    };
                    continue;
                }

                var preview = PatchHelper.PreviewPatch(original, body, context.JsonOptions);
                if (preview is not null)
                {
                    var validationResult = EntityValidator.Validate(preview, context.JsonOptions.PropertyNamingPolicy);
                    if (!validationResult.IsValid)
                    {
                        RestLibLogMessages.BatchPatchItemValidationFailed(context.Logger, index);
                        results[index] = new BatchItemResult
                        {
                            Index = index,
                            Status = StatusCodes.Status400BadRequest,
                            Error = ProblemDetailsFactory.ValidationFailed(
                                validationResult.Errors,
                                context.HttpContext.Request.Path)
                        };
                        continue;
                    }
                }

                itemsToPersist.Add((index, id, body));
            }
        }

        if (itemsToPersist.Count > 0)
        {
            var patches = itemsToPersist
                .Select(v => (v.Id, v.Body))
                .ToList();
            var patched = await context.BatchRepository!.PatchManyAsync(patches, context.CancellationToken);

            await ProcessBulkResultsAsync(itemsToPersist, patched, results, context);

            RestLibLogMessages.BatchPatchCompleted(context.Logger, patched.Count);
        }
    }

    /// <inheritdoc/>
    protected override async Task PersistSingleItemAsync(
        (int Index, TKey Id, JsonElement Body) validItem,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        var (index, id, body) = validItem;

        // Pre-persist validation: fetch original, preview merge, validate before persisting
        if (context.Options.EnableValidation)
        {
            var original = await context.Repository.GetByIdAsync(id, context.CancellationToken);
            if (original is null)
            {
                var entityName = typeof(TEntity).Name;
                RestLibLogMessages.BatchPatchItemNotFound(context.Logger, index, entityName, id!);
                results[index] = new BatchItemResult
                {
                    Index = index,
                    Status = StatusCodes.Status404NotFound,
                    Error = ProblemDetailsFactory.NotFound(entityName, id!, context.HttpContext.Request.Path)
                };
                return;
            }

            var preview = PatchHelper.PreviewPatch(original, body, context.JsonOptions);
            if (preview is not null)
            {
                var validationResult = EntityValidator.Validate(preview, context.JsonOptions.PropertyNamingPolicy);
                if (!validationResult.IsValid)
                {
                    RestLibLogMessages.BatchPatchItemValidationFailed(context.Logger, index);
                    results[index] = new BatchItemResult
                    {
                        Index = index,
                        Status = StatusCodes.Status400BadRequest,
                        Error = ProblemDetailsFactory.ValidationFailed(
                            validationResult.Errors,
                            context.HttpContext.Request.Path)
                    };
                    return;
                }
            }
        }

        TEntity? patched;
        try
        {
            patched = await context.Repository.PatchAsync(id, body, context.CancellationToken);
        }
        catch (Exception ex) when (IsPatchValidationException(ex))
        {
            results[index] = BadRequestResult(index, ex.Message, context.HttpContext.Request.Path);
            return;
        }

        if (patched is null)
        {
            var entityName = typeof(TEntity).Name;
            RestLibLogMessages.BatchPatchItemNotFound(context.Logger, index, entityName, id!);
            results[index] = new BatchItemResult
            {
                Index = index,
                Status = StatusCodes.Status404NotFound,
                Error = ProblemDetailsFactory.NotFound(entityName, id!, context.HttpContext.Request.Path)
            };
            return;
        }

        results[index] = await RunAfterPersistAndBuildResultAsync(index, patched, id, context);
    }

    private static bool IsPatchValidationException(Exception exception)
    {
        return exception.GetType().FullName == "RestLib.EntityFrameworkCore.EfCorePatchValidationException";
    }
}
