using Microsoft.AspNetCore.Http;
using RestLib.Logging;

namespace RestLib.Batch;

/// <summary>
/// Batch delete pipeline for mapped API and DB models.
/// </summary>
/// <typeparam name="TApiModel">The API model type.</typeparam>
/// <typeparam name="TDbModel">The DB model type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class MappedBatchDeletePipeline<TApiModel, TDbModel, TKey>
    : MappedBatchActionPipeline<TApiModel, TDbModel, TKey, TKey, (int Index, TKey Key)>
    where TApiModel : class
    where TDbModel : class
    where TKey : notnull
{
    /// <inheritdoc/>
    protected override int SuccessStatusCode => StatusCodes.Status204NoContent;

    /// <inheritdoc/>
    protected override RestLibOperation Operation => RestLibOperation.BatchDelete;

    /// <inheritdoc/>
    protected override string DeserializationErrorMessage =>
        "The 'items' array could not be deserialized as a list of IDs.";

    /// <inheritdoc/>
    protected override async Task<(BatchItemResult? Error, (int Index, TKey Key) ValidItem)> ValidateItemAsync(
        int index,
        TKey? key,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        if (key is null)
        {
            return (BadRequestResult(index, $"Item at index {index} has a null or invalid ID.", context.HttpContext.Request.Path), default);
        }

        if (context.DbPipeline is not null)
        {
            var hookContext = context.DbPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchDelete,
                resourceId: key);

            var hookError = await RunRequestHooksAsync(index, context.DbPipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, default);
            }
        }
        else if (context.ApiPipeline is not null)
        {
            var hookContext = context.ApiPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchDelete,
                resourceId: key);

            var hookError = await RunRequestHooksAsync(index, context.ApiPipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, default);
            }
        }

        return (null, (index, key));
    }

    /// <inheritdoc/>
    protected override int GetIndex((int Index, TKey Key) validItem) => validItem.Index;

    /// <inheritdoc/>
    protected override TKey? GetResourceId((int Index, TKey Key) validItem) => validItem.Key;

    /// <inheritdoc/>
    protected override async Task PersistBulkAsync(
        List<(int Index, TKey Key)> validItems,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var keys = validItems.Select(item => item.Key).ToList();
        var existingEntities = await context.BatchRepository!.GetByIdsAsync(keys, context.CancellationToken);
        var itemsToDelete = new List<(int Index, TKey Key, TApiModel? ApiEntity, TDbModel? DbEntity)>();

        foreach (var (index, key) in validItems)
        {
            if (!existingEntities.TryGetValue(key, out var existingDb))
            {
                RestLibLogMessages.BatchDeleteItemNotFound(context.Logger, index, typeof(TApiModel).Name, key!);
                results[index] = NotFoundResult(index, key!, context.HttpContext.Request.Path);
                continue;
            }

            TApiModel? apiEntity = null;
            if (context.DbPipeline is not null)
            {
                var hookContext = context.DbPipeline.CreateContext(
                    context.HttpContext,
                    RestLibOperation.BatchDelete,
                    resourceId: key,
                    entity: existingDb);

                var hookError = await RunBeforePersistHookAsync(index, context.DbPipeline, hookContext);
                if (hookError is not null)
                {
                    results[index] = hookError;
                    continue;
                }

                existingDb = hookContext.Entity ?? existingDb;
            }
            else if (context.ApiPipeline is not null)
            {
                apiEntity = context.Mapper.ToApi(existingDb);
                var hookContext = context.ApiPipeline.CreateContext(
                    context.HttpContext,
                    RestLibOperation.BatchDelete,
                    resourceId: key,
                    entity: apiEntity);

                var hookError = await RunBeforePersistHookAsync(index, context.ApiPipeline, hookContext);
                if (hookError is not null)
                {
                    results[index] = hookError;
                    continue;
                }

                apiEntity = hookContext.Entity ?? apiEntity;
            }

            itemsToDelete.Add((index, key, apiEntity, existingDb));
        }

        if (itemsToDelete.Count == 0)
        {
            return;
        }

        var keysToDelete = itemsToDelete.Select(item => item.Key).ToList();
        await context.BatchRepository!.DeleteManyAsync(keysToDelete, context.CancellationToken);

        RestLibLogMessages.BatchDeleteCompleted(context.Logger, keysToDelete.Count);

        foreach (var (index, key, apiEntity, dbEntity) in itemsToDelete)
        {
            if (context.DbPipeline is not null)
            {
                var afterContext = context.DbPipeline.CreateContext(
                    context.HttpContext,
                    RestLibOperation.BatchDelete,
                    resourceId: key,
                    entity: dbEntity);
                var shouldContinue = await context.DbPipeline.ExecuteAfterPersistAsync(afterContext);
                if (!shouldContinue)
                {
                    results[index] = BuildHookResultItem(index, afterContext.EarlyResult, context.HttpContext);
                    continue;
                }
            }
            else if (context.ApiPipeline is not null)
            {
                var afterContext = context.ApiPipeline.CreateContext(
                    context.HttpContext,
                    RestLibOperation.BatchDelete,
                    resourceId: key,
                    entity: apiEntity ?? (dbEntity is not null ? context.Mapper.ToApi(dbEntity) : null));
                var shouldContinue = await context.ApiPipeline.ExecuteAfterPersistAsync(afterContext);
                if (!shouldContinue)
                {
                    results[index] = BuildHookResultItem(index, afterContext.EarlyResult, context.HttpContext);
                    continue;
                }
            }

            results[index] = new BatchItemResult
            {
                Index = index,
                Status = StatusCodes.Status204NoContent
            };
        }
    }

    /// <inheritdoc/>
    protected override async Task PersistSingleItemAsync(
        (int Index, TKey Key) validItem,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var (index, key) = validItem;
        var entityToDeleteDb = await context.Repository.GetByIdAsync(key, context.CancellationToken);
        if (entityToDeleteDb is null)
        {
            RestLibLogMessages.BatchDeleteItemNotFound(context.Logger, index, typeof(TApiModel).Name, key!);
            results[index] = NotFoundResult(index, key!, context.HttpContext.Request.Path);
            return;
        }

        TApiModel? entityToDeleteApi = null;
        if (context.DbPipeline is not null)
        {
            var hookContext = context.DbPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchDelete,
                resourceId: key,
                entity: entityToDeleteDb);

            var hookError = await RunBeforePersistHookAsync(index, context.DbPipeline, hookContext);
            if (hookError is not null)
            {
                results[index] = hookError;
                return;
            }

            entityToDeleteDb = hookContext.Entity ?? entityToDeleteDb;
        }
        else if (context.ApiPipeline is not null)
        {
            entityToDeleteApi = context.Mapper.ToApi(entityToDeleteDb);
            var hookContext = context.ApiPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchDelete,
                resourceId: key,
                entity: entityToDeleteApi);

            var hookError = await RunBeforePersistHookAsync(index, context.ApiPipeline, hookContext);
            if (hookError is not null)
            {
                results[index] = hookError;
                return;
            }

            entityToDeleteApi = hookContext.Entity ?? entityToDeleteApi;
        }

        var deleted = await context.Repository.DeleteAsync(key, context.CancellationToken);
        if (!deleted)
        {
            RestLibLogMessages.BatchDeleteItemNotFound(context.Logger, index, typeof(TApiModel).Name, key!);
            results[index] = NotFoundResult(index, key!, context.HttpContext.Request.Path);
            return;
        }

        if (context.DbPipeline is not null)
        {
            var afterContext = context.DbPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchDelete,
                resourceId: key,
                entity: entityToDeleteDb);
            var shouldContinue = await context.DbPipeline.ExecuteAfterPersistAsync(afterContext);
            if (!shouldContinue)
            {
                results[index] = BuildHookResultItem(index, afterContext.EarlyResult, context.HttpContext);
                return;
            }
        }
        else if (context.ApiPipeline is not null)
        {
            var afterContext = context.ApiPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchDelete,
                resourceId: key,
                entity: entityToDeleteApi ?? context.Mapper.ToApi(entityToDeleteDb));
            var shouldContinue = await context.ApiPipeline.ExecuteAfterPersistAsync(afterContext);
            if (!shouldContinue)
            {
                results[index] = BuildHookResultItem(index, afterContext.EarlyResult, context.HttpContext);
                return;
            }
        }

        results[index] = new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status204NoContent
        };
    }
}
