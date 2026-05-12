using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Endpoints;
using RestLib.Logging;

namespace RestLib.Batch;

/// <summary>
/// Batch patch pipeline for mapped API and DB models.
/// </summary>
/// <typeparam name="TApiModel">The API model type.</typeparam>
/// <typeparam name="TDbModel">The DB model type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class MappedBatchPatchPipeline<TApiModel, TDbModel, TKey>
    : MappedBatchActionPipeline<TApiModel, TDbModel, TKey, BatchUpdateItem<TKey>, (int Index, TKey Id, JsonElement Body)>
    where TApiModel : class
    where TDbModel : class
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
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        if (item is null)
        {
            return (BadRequestResult(index, $"Item at index {index} could not be deserialized.", context.HttpContext.Request.Path), default);
        }

        var existingDb = await context.Repository.GetByIdAsync(item.Id, context.CancellationToken);
        if (existingDb is null)
        {
            return (NotFoundResult(index, item.Id!, context.HttpContext.Request.Path), default);
        }

        if (context.DbPipeline is not null)
        {
            var hookContext = context.DbPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchPatch,
                resourceId: item.Id);

            var received = await context.DbPipeline.ExecuteOnRequestReceivedAsync(hookContext);
            if (!received)
            {
                return (HookShortCircuitResult(index, hookContext), default);
            }
        }
        else if (context.ApiPipeline is not null)
        {
            var hookContext = context.ApiPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchPatch,
                resourceId: item.Id);

            var received = await context.ApiPipeline.ExecuteOnRequestReceivedAsync(hookContext);
            if (!received)
            {
                return (HookShortCircuitResult(index, hookContext), default);
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
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var ids = validItems.Select(item => item.Id).ToList();
        var originals = await context.BatchRepository!.GetByIdsAsync(ids, context.CancellationToken);

        var itemsToPersist = new List<(int Index, TKey Id, TDbModel Entity)>();

        foreach (var (index, id, body) in validItems)
        {
            if (!originals.TryGetValue(id, out var originalDb))
            {
                RestLibLogMessages.BatchPatchItemNotFound(context.Logger, index, typeof(TApiModel).Name, id!);
                results[index] = NotFoundResult(index, id!, context.HttpContext.Request.Path);
                continue;
            }

            var originalApi = context.Mapper.ToApi(originalDb);
            var patchedApi = PatchHelper.PreviewPatch(originalApi, body, context.JsonOptions, context.Logger);
            if (patchedApi is null)
            {
                results[index] = BadRequestResult(
                    index,
                    "The patch document could not be applied to the resource.",
                    context.HttpContext.Request.Path);
                continue;
            }

            var validationError = ValidateApiEntity(index, patchedApi, context);
            if (validationError is not null)
            {
                RestLibLogMessages.BatchPatchItemValidationFailed(context.Logger, index);
                results[index] = validationError;
                continue;
            }

            TDbModel persistedDb;
            if (context.DbPipeline is not null)
            {
                persistedDb = context.Mapper.ToDb(patchedApi);
                var hookContext = context.DbPipeline.CreateContext(
                    context.HttpContext,
                    RestLibOperation.BatchPatch,
                    resourceId: id,
                    entity: persistedDb,
                    originalEntity: originalDb);

                var validated = await context.DbPipeline.ExecuteOnRequestValidatedAsync(hookContext);
                if (!validated)
                {
                    results[index] = HookShortCircuitResult(index, hookContext);
                    continue;
                }

                persistedDb = hookContext.Entity ?? persistedDb;
                patchedApi = context.Mapper.ToApi(persistedDb);

                validationError = ValidateApiEntity(index, patchedApi, context);
                if (validationError is not null)
                {
                    RestLibLogMessages.BatchPatchItemValidationFailed(context.Logger, index);
                    results[index] = validationError;
                    continue;
                }

                hookContext.Entity = persistedDb;
                hookContext.SetOriginalEntity(originalDb);

                var hookError = await RunBeforePersistHookAsync(index, context.DbPipeline, hookContext);
                if (hookError is not null)
                {
                    results[index] = hookError;
                    continue;
                }

                persistedDb = hookContext.Entity ?? persistedDb;
            }
            else if (context.ApiPipeline is not null)
            {
                var hookContext = context.ApiPipeline.CreateContext(
                    context.HttpContext,
                    RestLibOperation.BatchPatch,
                    resourceId: id,
                    entity: patchedApi,
                    originalEntity: originalApi);

                var validated = await context.ApiPipeline.ExecuteOnRequestValidatedAsync(hookContext);
                if (!validated)
                {
                    results[index] = HookShortCircuitResult(index, hookContext);
                    continue;
                }

                patchedApi = hookContext.Entity ?? patchedApi;

                validationError = ValidateApiEntity(index, patchedApi, context);
                if (validationError is not null)
                {
                    RestLibLogMessages.BatchPatchItemValidationFailed(context.Logger, index);
                    results[index] = validationError;
                    continue;
                }

                hookContext.Entity = patchedApi;
                hookContext.SetOriginalEntity(originalApi);

                var hookError = await RunBeforePersistHookAsync(index, context.ApiPipeline, hookContext);
                if (hookError is not null)
                {
                    results[index] = hookError;
                    continue;
                }

                patchedApi = hookContext.Entity ?? patchedApi;
                persistedDb = context.Mapper.ToDb(patchedApi);
            }
            else
            {
                persistedDb = context.Mapper.ToDb(patchedApi);
            }

            _ = TrySetDbEntityKey(persistedDb, id, context);
            itemsToPersist.Add((index, id, persistedDb));
        }

        if (itemsToPersist.Count == 0)
        {
            return;
        }

        var entities = itemsToPersist.Select(item => item.Entity).ToList();
        var updated = await context.BatchRepository!.UpdateManyAsync(entities, context.CancellationToken);

        for (var i = 0; i < itemsToPersist.Count; i++)
        {
            var item = itemsToPersist[i];
            results[item.Index] = await RunAfterPersistAndBuildResultAsync(item.Index, updated[i], item.Id, context);
        }

        RestLibLogMessages.BatchPatchCompleted(context.Logger, updated.Count);
    }

    /// <inheritdoc/>
    protected override async Task PersistSingleItemAsync(
        (int Index, TKey Id, JsonElement Body) validItem,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var (index, id, body) = validItem;

        var originalDb = await context.Repository.GetByIdAsync(id, context.CancellationToken);
        if (originalDb is null)
        {
            RestLibLogMessages.BatchPatchItemNotFound(context.Logger, index, typeof(TApiModel).Name, id!);
            results[index] = NotFoundResult(index, id!, context.HttpContext.Request.Path);
            return;
        }

        var originalApi = context.Mapper.ToApi(originalDb);
        var patchedApi = PatchHelper.PreviewPatch(originalApi, body, context.JsonOptions, context.Logger);
        if (patchedApi is null)
        {
            results[index] = BadRequestResult(
                index,
                "The patch document could not be applied to the resource.",
                context.HttpContext.Request.Path);
            return;
        }

        var validationError = ValidateApiEntity(index, patchedApi, context);
        if (validationError is not null)
        {
            RestLibLogMessages.BatchPatchItemValidationFailed(context.Logger, index);
            results[index] = validationError;
            return;
        }

        TDbModel persistedDb;
        if (context.DbPipeline is not null)
        {
            persistedDb = context.Mapper.ToDb(patchedApi);
            var hookContext = context.DbPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchPatch,
                resourceId: id,
                entity: persistedDb,
                originalEntity: originalDb);

            var validated = await context.DbPipeline.ExecuteOnRequestValidatedAsync(hookContext);
            if (!validated)
            {
                results[index] = HookShortCircuitResult(index, hookContext);
                return;
            }

            persistedDb = hookContext.Entity ?? persistedDb;
            patchedApi = context.Mapper.ToApi(persistedDb);

            validationError = ValidateApiEntity(index, patchedApi, context);
            if (validationError is not null)
            {
                RestLibLogMessages.BatchPatchItemValidationFailed(context.Logger, index);
                results[index] = validationError;
                return;
            }

            hookContext.Entity = persistedDb;
            hookContext.SetOriginalEntity(originalDb);

            var hookError = await RunBeforePersistHookAsync(index, context.DbPipeline, hookContext);
            if (hookError is not null)
            {
                results[index] = hookError;
                return;
            }

            persistedDb = hookContext.Entity ?? persistedDb;
        }
        else if (context.ApiPipeline is not null)
        {
            var hookContext = context.ApiPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchPatch,
                resourceId: id,
                entity: patchedApi,
                originalEntity: originalApi);

            var validated = await context.ApiPipeline.ExecuteOnRequestValidatedAsync(hookContext);
            if (!validated)
            {
                results[index] = HookShortCircuitResult(index, hookContext);
                return;
            }

            patchedApi = hookContext.Entity ?? patchedApi;

            validationError = ValidateApiEntity(index, patchedApi, context);
            if (validationError is not null)
            {
                RestLibLogMessages.BatchPatchItemValidationFailed(context.Logger, index);
                results[index] = validationError;
                return;
            }

            hookContext.Entity = patchedApi;
            hookContext.SetOriginalEntity(originalApi);

            var hookError = await RunBeforePersistHookAsync(index, context.ApiPipeline, hookContext);
            if (hookError is not null)
            {
                results[index] = hookError;
                return;
            }

            patchedApi = hookContext.Entity ?? patchedApi;
            persistedDb = context.Mapper.ToDb(patchedApi);
        }
        else
        {
            persistedDb = context.Mapper.ToDb(patchedApi);
        }

        _ = TrySetDbEntityKey(persistedDb, id, context);
        var updated = await context.Repository.UpdateAsync(id, persistedDb, context.CancellationToken);
        if (updated is null)
        {
            RestLibLogMessages.BatchPatchItemNotFound(context.Logger, index, typeof(TApiModel).Name, id!);
            results[index] = NotFoundResult(index, id!, context.HttpContext.Request.Path);
            return;
        }

        results[index] = await RunAfterPersistAndBuildResultAsync(index, updated, id, context);
    }
}
