using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Logging;

namespace RestLib.Batch;

/// <summary>
/// Batch create pipeline. Deserializes entities, validates, persists via
/// <see cref="IRepository{TEntity, TKey}.CreateAsync"/> or
/// <see cref="IBatchRepository{TEntity, TKey}.CreateManyAsync"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class BatchCreatePipeline<TEntity, TKey>
    : BatchActionPipeline<TEntity, TKey, TEntity, (int Index, TEntity Entity)>
    where TEntity : class
    where TKey : notnull
{
    /// <inheritdoc/>
    protected override int SuccessStatusCode => StatusCodes.Status201Created;

    /// <inheritdoc/>
    protected override RestLibOperation Operation => RestLibOperation.BatchCreate;

    /// <inheritdoc/>
    protected override async Task<(BatchItemResult? Error, (int Index, TEntity Entity) ValidItem)> ValidateItemAsync(
        int index,
        TEntity? entity,
        BatchContext<TEntity, TKey> context)
    {
        if (entity is null)
            return (BadRequestResult(index, $"Item at index {index} could not be deserialized.", context.HttpContext.Request.Path), default);

        // Validation
        var validationError = ValidateEntity(index, entity, context);
        if (validationError is not null) return (validationError, default);

        // Hooks: OnRequestReceived, OnRequestValidated, BeforePersist
        if (context.Pipeline is not null)
        {
            var hookContext = context.Pipeline.CreateContext(
                context.HttpContext, RestLibOperation.BatchCreate, entity: entity);

            var hookError = await RunPrePersistHooksAsync(index, context.Pipeline, hookContext);
            if (hookError is not null) return (hookError, default);

            entity = hookContext.Entity ?? entity;
        }

        return (null, (index, entity));
    }

    /// <inheritdoc/>
    protected override int GetIndex((int Index, TEntity Entity) validItem) => validItem.Index;

    /// <inheritdoc/>
    protected override TEntity? GetEntity((int Index, TEntity Entity) validItem) => validItem.Entity;

    /// <inheritdoc/>
    protected override async Task PersistBulkAsync(
        List<(int Index, TEntity Entity)> validItems,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        var entities = validItems.Select(v => v.Entity).ToList();
        var created = await context.BatchRepository!.CreateManyAsync(entities, context.CancellationToken);

        await ProcessBulkResultsAsync(validItems, created, results, context);

        RestLibLogMessages.BatchCreateCompleted(context.Logger, created.Count);
    }

    /// <inheritdoc/>
    protected override async Task PersistSingleItemAsync(
        (int Index, TEntity Entity) validItem,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        var (index, entity) = validItem;
        var created = await context.Repository.CreateAsync(entity, context.CancellationToken);

        results[index] = await RunAfterPersistAndBuildResultAsync(index, created, default, context);
    }
}
