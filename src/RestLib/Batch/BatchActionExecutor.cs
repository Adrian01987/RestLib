using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Responses;
using RestLib.Validation;

namespace RestLib.Batch;

/// <summary>
/// Handles persistence and post-persist processing for batch operations.
/// Supports both bulk paths (via <see cref="IBatchRepository{TEntity, TKey}"/>)
/// and single-operation fallback paths.
/// </summary>
internal static class BatchActionExecutor
{
    /// <summary>
    /// Persists created entities and runs AfterPersist hooks.
    /// Uses the batch repository bulk path when available; otherwise falls back
    /// to individual <see cref="IRepository{TEntity, TKey}.CreateAsync"/> calls.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="validItems">The validated items with their original indices.</param>
    /// <param name="results">The results array to populate.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="batchRepository">The optional batch-optimized repository.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task ExecuteCreatesAsync<TEntity, TKey>(
        List<(int Index, TEntity Entity)> validItems,
        BatchItemResult?[] results,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        IBatchRepository<TEntity, TKey>? batchRepository,
        HookPipeline<TEntity, TKey>? pipeline,
        CancellationToken ct)
        where TEntity : class
    {
        if (validItems.Count == 0)
        {
            return;
        }

        if (batchRepository is not null)
        {
            try
            {
                var entities = validItems.Select(v => v.Entity).ToList();
                var created = await batchRepository.CreateManyAsync(entities, ct);

                for (var j = 0; j < validItems.Count; j++)
                {
                    var (index, _) = validItems[j];
                    var createdEntity = created[j];

                    if (pipeline is not null)
                    {
                        var afterContext = pipeline.CreateContext(
                            httpContext, RestLibOperation.BatchCreate, entity: createdEntity);
                        await pipeline.ExecuteAfterPersistAsync(afterContext);
                    }

                    results[index] = new BatchItemResult
                    {
                        Index = index,
                        Status = StatusCodes.Status201Created,
                        Entity = createdEntity
                    };
                }
            }
            catch (Exception ex)
            {
                foreach (var (index, _) in validItems)
                {
                    results[index] = ExceptionResult(index, ex, httpContext.Request.Path);
                }
            }
        }
        else
        {
            foreach (var (index, entity) in validItems)
            {
                try
                {
                    var created = await repository.CreateAsync(entity, ct);

                    if (pipeline is not null)
                    {
                        var afterContext = pipeline.CreateContext(
                            httpContext, RestLibOperation.BatchCreate, entity: created);
                        await pipeline.ExecuteAfterPersistAsync(afterContext);
                    }

                    results[index] = new BatchItemResult
                    {
                        Index = index,
                        Status = StatusCodes.Status201Created,
                        Entity = created
                    };
                }
                catch (Exception ex)
                {
                    results[index] = ExceptionResult(index, ex, httpContext.Request.Path);
                }
            }
        }
    }

    /// <summary>
    /// Persists updated entities and runs AfterPersist hooks.
    /// Uses the batch repository bulk path when available; otherwise falls back
    /// to individual <see cref="IRepository{TEntity, TKey}.UpdateAsync"/> calls.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="validItems">The validated items with their original indices and IDs.</param>
    /// <param name="results">The results array to populate.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="batchRepository">The optional batch-optimized repository.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task ExecuteUpdatesAsync<TEntity, TKey>(
        List<(int Index, TKey Id, TEntity Entity)> validItems,
        BatchItemResult?[] results,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        IBatchRepository<TEntity, TKey>? batchRepository,
        HookPipeline<TEntity, TKey>? pipeline,
        CancellationToken ct)
        where TEntity : class
    {
        if (validItems.Count == 0)
        {
            return;
        }

        if (batchRepository is not null)
        {
            try
            {
                var entities = validItems.Select(v => v.Entity).ToList();
                var updated = await batchRepository.UpdateManyAsync(entities, ct);

                for (var j = 0; j < validItems.Count; j++)
                {
                    var (index, id, _) = validItems[j];
                    var updatedEntity = updated[j];

                    if (pipeline is not null)
                    {
                        var afterContext = pipeline.CreateContext(
                            httpContext, RestLibOperation.BatchUpdate,
                            resourceId: id, entity: updatedEntity);
                        await pipeline.ExecuteAfterPersistAsync(afterContext);
                    }

                    results[index] = new BatchItemResult
                    {
                        Index = index,
                        Status = StatusCodes.Status200OK,
                        Entity = updatedEntity
                    };
                }
            }
            catch (Exception ex)
            {
                foreach (var (index, _, _) in validItems)
                {
                    results[index] = ExceptionResult(index, ex, httpContext.Request.Path);
                }
            }
        }
        else
        {
            foreach (var (index, id, entity) in validItems)
            {
                try
                {
                    var updated = await repository.UpdateAsync(id, entity, ct);
                    if (updated is null)
                    {
                        var entityName = typeof(TEntity).Name;
                        results[index] = new BatchItemResult
                        {
                            Index = index,
                            Status = StatusCodes.Status404NotFound,
                            Error = ProblemDetailsFactory.NotFound(entityName, id!, httpContext.Request.Path)
                        };
                        continue;
                    }

                    if (pipeline is not null)
                    {
                        var afterContext = pipeline.CreateContext(
                            httpContext, RestLibOperation.BatchUpdate,
                            resourceId: id, entity: updated);
                        await pipeline.ExecuteAfterPersistAsync(afterContext);
                    }

                    results[index] = new BatchItemResult
                    {
                        Index = index,
                        Status = StatusCodes.Status200OK,
                        Entity = updated
                    };
                }
                catch (Exception ex)
                {
                    results[index] = ExceptionResult(index, ex, httpContext.Request.Path);
                }
            }
        }
    }

    /// <summary>
    /// Persists patches and runs AfterPersist hooks, with post-persist validation.
    /// Uses the batch repository bulk path when available; otherwise falls back
    /// to individual <see cref="IRepository{TEntity, TKey}.PatchAsync"/> calls.
    /// Patch validation is deferred to post-persist because the merged entity
    /// is only available after the patch is applied.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="validItems">The validated items with their original indices, IDs, and patch bodies.</param>
    /// <param name="results">The results array to populate.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="batchRepository">The optional batch-optimized repository.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="options">The global RestLib options.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task ExecutePatchesAsync<TEntity, TKey>(
        List<(int Index, TKey Id, JsonElement Body)> validItems,
        BatchItemResult?[] results,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        IBatchRepository<TEntity, TKey>? batchRepository,
        HookPipeline<TEntity, TKey>? pipeline,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
    {
        if (validItems.Count == 0)
        {
            return;
        }

        if (batchRepository is not null)
        {
            try
            {
                var patches = validItems
                    .Select(v => (v.Id, v.Body))
                    .ToList();
                var patched = await batchRepository.PatchManyAsync(patches, ct);

                for (var j = 0; j < validItems.Count; j++)
                {
                    var (index, id, _) = validItems[j];
                    var patchedEntity = patched[j];

                    // Validate patched entity (post-persist, consistent with single-item PATCH)
                    if (options.EnableValidation)
                    {
                        var validationResult = EntityValidator.Validate(patchedEntity, jsonOptions.PropertyNamingPolicy);
                        if (!validationResult.IsValid)
                        {
                            results[index] = new BatchItemResult
                            {
                                Index = index,
                                Status = StatusCodes.Status400BadRequest,
                                Error = ProblemDetailsFactory.ValidationFailed(
                                    new Dictionary<string, string[]>(validationResult.Errors),
                                    httpContext.Request.Path)
                            };
                            continue;
                        }
                    }

                    if (pipeline is not null)
                    {
                        var afterContext = pipeline.CreateContext(
                            httpContext, RestLibOperation.BatchPatch,
                            resourceId: id, entity: patchedEntity);
                        await pipeline.ExecuteAfterPersistAsync(afterContext);
                    }

                    results[index] = new BatchItemResult
                    {
                        Index = index,
                        Status = StatusCodes.Status200OK,
                        Entity = patchedEntity
                    };
                }
            }
            catch (Exception ex)
            {
                foreach (var (index, _, _) in validItems)
                {
                    results[index] = ExceptionResult(index, ex, httpContext.Request.Path);
                }
            }
        }
        else
        {
            foreach (var (index, id, body) in validItems)
            {
                try
                {
                    var patched = await repository.PatchAsync(id, body, ct);
                    if (patched is null)
                    {
                        var entityName = typeof(TEntity).Name;
                        results[index] = new BatchItemResult
                        {
                            Index = index,
                            Status = StatusCodes.Status404NotFound,
                            Error = ProblemDetailsFactory.NotFound(entityName, id!, httpContext.Request.Path)
                        };
                        continue;
                    }

                    // Validate patched entity (post-persist, consistent with single-item PATCH)
                    if (options.EnableValidation)
                    {
                        var validationResult = EntityValidator.Validate(patched, jsonOptions.PropertyNamingPolicy);
                        if (!validationResult.IsValid)
                        {
                            results[index] = new BatchItemResult
                            {
                                Index = index,
                                Status = StatusCodes.Status400BadRequest,
                                Error = ProblemDetailsFactory.ValidationFailed(
                                    new Dictionary<string, string[]>(validationResult.Errors),
                                    httpContext.Request.Path)
                            };
                            continue;
                        }
                    }

                    if (pipeline is not null)
                    {
                        var afterContext = pipeline.CreateContext(
                            httpContext, RestLibOperation.BatchPatch,
                            resourceId: id, entity: patched);
                        await pipeline.ExecuteAfterPersistAsync(afterContext);
                    }

                    results[index] = new BatchItemResult
                    {
                        Index = index,
                        Status = StatusCodes.Status200OK,
                        Entity = patched
                    };
                }
                catch (Exception ex)
                {
                    results[index] = ExceptionResult(index, ex, httpContext.Request.Path);
                }
            }
        }
    }

    /// <summary>
    /// Persists deletes and runs AfterPersist hooks.
    /// Uses the batch repository bulk path when available; otherwise falls back
    /// to individual <see cref="IRepository{TEntity, TKey}.DeleteAsync"/> calls.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="validKeys">The validated keys with their original indices.</param>
    /// <param name="results">The results array to populate.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="batchRepository">The optional batch-optimized repository.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task ExecuteDeletesAsync<TEntity, TKey>(
        List<(int Index, TKey Key)> validKeys,
        BatchItemResult?[] results,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        IBatchRepository<TEntity, TKey>? batchRepository,
        HookPipeline<TEntity, TKey>? pipeline,
        CancellationToken ct)
        where TEntity : class
    {
        if (validKeys.Count == 0)
        {
            return;
        }

        if (batchRepository is not null)
        {
            try
            {
                var keysToDelete = validKeys.Select(v => v.Key).ToList();
                await batchRepository.DeleteManyAsync(keysToDelete, ct);

                // DeleteManyAsync returns total count — we don't know which individual
                // keys were not found. Mark all as succeeded (204) since the bulk
                // operation was accepted. For fine-grained 404 detection, callers
                // should fall back to the single-op path (i.e., don't register
                // IBatchRepository if per-item 404s are required).
                for (var j = 0; j < validKeys.Count; j++)
                {
                    var (index, key) = validKeys[j];

                    if (pipeline is not null)
                    {
                        var afterContext = pipeline.CreateContext(
                            httpContext, RestLibOperation.BatchDelete, resourceId: key);
                        await pipeline.ExecuteAfterPersistAsync(afterContext);
                    }

                    results[index] = new BatchItemResult
                    {
                        Index = index,
                        Status = StatusCodes.Status204NoContent
                    };
                }
            }
            catch (Exception ex)
            {
                foreach (var (index, _) in validKeys)
                {
                    results[index] = ExceptionResult(index, ex, httpContext.Request.Path);
                }
            }
        }
        else
        {
            foreach (var (index, key) in validKeys)
            {
                try
                {
                    var deleted = await repository.DeleteAsync(key, ct);
                    if (!deleted)
                    {
                        var entityName = typeof(TEntity).Name;
                        results[index] = new BatchItemResult
                        {
                            Index = index,
                            Status = StatusCodes.Status404NotFound,
                            Error = ProblemDetailsFactory.NotFound(entityName, key!, httpContext.Request.Path)
                        };
                        continue;
                    }

                    if (pipeline is not null)
                    {
                        var afterContext = pipeline.CreateContext(
                            httpContext, RestLibOperation.BatchDelete, resourceId: key);
                        await pipeline.ExecuteAfterPersistAsync(afterContext);
                    }

                    results[index] = new BatchItemResult
                    {
                        Index = index,
                        Status = StatusCodes.Status204NoContent
                    };
                }
                catch (Exception ex)
                {
                    results[index] = ExceptionResult(index, ex, httpContext.Request.Path);
                }
            }
        }
    }

    /// <summary>
    /// Creates a batch item result from an exception.
    /// Includes the exception type and message in the problem details to
    /// aid debugging; callers should avoid exposing raw stack traces.
    /// </summary>
    private static BatchItemResult ExceptionResult(
        int index,
        Exception ex,
        string? instance)
    {
        var detail = $"{ex.GetType().Name}: {ex.Message}";
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status500InternalServerError,
            Error = ProblemDetailsFactory.InternalError(detail: detail, instance: instance)
        };
    }
}
