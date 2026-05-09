using Microsoft.AspNetCore.Http;
using RestLib.Logging;

namespace RestLib.Batch;

/// <summary>
/// Batch create pipeline for mapped API and DB models.
/// </summary>
/// <typeparam name="TApiModel">The API model type.</typeparam>
/// <typeparam name="TDbModel">The DB model type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class MappedBatchCreatePipeline<TApiModel, TDbModel, TKey>
    : MappedBatchActionPipeline<TApiModel, TDbModel, TKey, TApiModel, (int Index, TApiModel ApiEntity, TDbModel DbEntity)>
    where TApiModel : class
    where TDbModel : class
    where TKey : notnull
{
    /// <inheritdoc/>
    protected override int SuccessStatusCode => StatusCodes.Status201Created;

    /// <inheritdoc/>
    protected override RestLibOperation Operation => RestLibOperation.BatchCreate;

    /// <inheritdoc/>
    protected override async Task<(BatchItemResult? Error, (int Index, TApiModel ApiEntity, TDbModel DbEntity) ValidItem)> ValidateItemAsync(
        int index,
        TApiModel? apiEntity,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        if (apiEntity is null)
        {
            return (BadRequestResult(index, $"Item at index {index} could not be deserialized.", context.HttpContext.Request.Path), default);
        }

        if (context.DbPipeline is not null)
        {
            var dbEntity = context.Mapper.ToDb(apiEntity);
            var hookContext = context.DbPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchCreate,
                entity: dbEntity);

            var received = await context.DbPipeline.ExecuteOnRequestReceivedAsync(hookContext);
            if (!received)
            {
                return (HookShortCircuitResult(index, hookContext), default);
            }

            dbEntity = hookContext.Entity ?? dbEntity;
            apiEntity = context.Mapper.ToApi(dbEntity);

            var validationError = ValidateApiEntity(index, apiEntity, context);
            if (validationError is not null)
            {
                return (validationError, default);
            }

            hookContext.Entity = dbEntity;
            var validated = await context.DbPipeline.ExecuteOnRequestValidatedAsync(hookContext);
            if (!validated)
            {
                return (HookShortCircuitResult(index, hookContext), default);
            }

            dbEntity = hookContext.Entity ?? dbEntity;
            apiEntity = context.Mapper.ToApi(dbEntity);

            validationError = ValidateApiEntity(index, apiEntity, context);
            if (validationError is not null)
            {
                return (validationError, default);
            }

            hookContext.Entity = dbEntity;
            var hookError = await RunBeforePersistHookAsync(index, context.DbPipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, default);
            }

            dbEntity = hookContext.Entity ?? dbEntity;
            apiEntity = context.Mapper.ToApi(dbEntity);
            return (null, (index, apiEntity, dbEntity));
        }

        if (context.ApiPipeline is not null)
        {
            var hookContext = context.ApiPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchCreate,
                entity: apiEntity);

            var received = await context.ApiPipeline.ExecuteOnRequestReceivedAsync(hookContext);
            if (!received)
            {
                return (HookShortCircuitResult(index, hookContext), default);
            }

            apiEntity = hookContext.Entity ?? apiEntity;

            var validationError = ValidateApiEntity(index, apiEntity, context);
            if (validationError is not null)
            {
                return (validationError, default);
            }

            hookContext.Entity = apiEntity;
            var validated = await context.ApiPipeline.ExecuteOnRequestValidatedAsync(hookContext);
            if (!validated)
            {
                return (HookShortCircuitResult(index, hookContext), default);
            }

            apiEntity = hookContext.Entity ?? apiEntity;

            validationError = ValidateApiEntity(index, apiEntity, context);
            if (validationError is not null)
            {
                return (validationError, default);
            }

            hookContext.Entity = apiEntity;
            var hookError = await RunBeforePersistHookAsync(index, context.ApiPipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, default);
            }

            apiEntity = hookContext.Entity ?? apiEntity;
            return (null, (index, apiEntity, context.Mapper.ToDb(apiEntity)));
        }

        var directValidationError = ValidateApiEntity(index, apiEntity, context);
        if (directValidationError is not null)
        {
            return (directValidationError, default);
        }

        return (null, (index, apiEntity, context.Mapper.ToDb(apiEntity)));
    }

    /// <inheritdoc/>
    protected override int GetIndex((int Index, TApiModel ApiEntity, TDbModel DbEntity) validItem) => validItem.Index;

    /// <inheritdoc/>
    protected override TApiModel? GetApiEntity((int Index, TApiModel ApiEntity, TDbModel DbEntity) validItem) => validItem.ApiEntity;

    /// <inheritdoc/>
    protected override TDbModel? GetDbEntity((int Index, TApiModel ApiEntity, TDbModel DbEntity) validItem) => validItem.DbEntity;

    /// <inheritdoc/>
    protected override async Task PersistBulkAsync(
        List<(int Index, TApiModel ApiEntity, TDbModel DbEntity)> validItems,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var entities = validItems.Select(item => item.DbEntity).ToList();
        var created = await context.BatchRepository!.CreateManyAsync(entities, context.CancellationToken);

        await ProcessBulkResultsAsync(validItems, created, results, context);

        RestLibLogMessages.BatchCreateCompleted(context.Logger, created.Count);
    }

    /// <inheritdoc/>
    protected override async Task PersistSingleItemAsync(
        (int Index, TApiModel ApiEntity, TDbModel DbEntity) validItem,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var (index, _, dbEntity) = validItem;
        var created = await context.Repository.CreateAsync(dbEntity, context.CancellationToken);

        results[index] = await RunAfterPersistAndBuildResultAsync(index, created, default, context);
    }
}
