using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Responses;
using RestLib.Validation;

namespace RestLib.Batch;

/// <summary>
/// Orchestrates the processing of batch requests, handling per-item
/// validation, hooks, and repository operations. When an
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
    /// <param name="config">The endpoint configuration.</param>
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
        RestLibEndpointConfiguration<TEntity, TKey> config,
        HookPipeline<TEntity, TKey>? pipeline,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
    {
        var items = DeserializeArray<TEntity>(itemsElement, jsonOptions);
        if (items is null)
        {
            return SingleErrorResponse(0, StatusCodes.Status400BadRequest,
                ProblemDetailsFactory.BadRequest(
                    "The 'items' array could not be deserialized.",
                    httpContext.Request.Path));
        }

        var results = new BatchItemResult?[items.Count];

        // Phase 1: Validate all items and run pre-persist hooks, collecting valid ones.
        var validItems = new List<(int Index, TEntity Entity)>();

        for (var i = 0; i < items.Count; i++)
        {
            var entity = items[i];
            if (entity is null)
            {
                results[i] = new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} could not be deserialized.",
                        httpContext.Request.Path)
                };
                continue;
            }

            // Validation
            if (options.EnableValidation)
            {
                var validationResult = EntityValidator.Validate(entity, jsonOptions.PropertyNamingPolicy);
                if (!validationResult.IsValid)
                {
                    results[i] = new BatchItemResult
                    {
                        Index = i,
                        Status = StatusCodes.Status400BadRequest,
                        Error = ProblemDetailsFactory.ValidationFailed(
                            new Dictionary<string, string[]>(validationResult.Errors),
                            httpContext.Request.Path)
                    };
                    continue;
                }
            }

            // Hooks: OnRequestReceived, OnRequestValidated, BeforePersist
            if (pipeline is not null)
            {
                var hookContext = pipeline.CreateContext(
                    httpContext, RestLibOperation.BatchCreate, entity: entity);

                var received = await pipeline.ExecuteOnRequestReceivedAsync(hookContext);
                if (!received)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }

                var validated = await pipeline.ExecuteOnRequestValidatedAsync(hookContext);
                if (!validated)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }

                var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
                if (!before)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }

                entity = hookContext.Entity ?? entity;
            }

            validItems.Add((i, entity));
        }

        // Phase 2 & 3: Persist and run AfterPersist hooks.
        if (batchRepository is not null && validItems.Count > 0)
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
    /// <param name="config">The endpoint configuration.</param>
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
        RestLibEndpointConfiguration<TEntity, TKey> config,
        HookPipeline<TEntity, TKey>? pipeline,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
    {
        var items = DeserializeArray<BatchUpdateItem<TKey>>(itemsElement, jsonOptions);
        if (items is null)
        {
            return SingleErrorResponse(0, StatusCodes.Status400BadRequest,
                ProblemDetailsFactory.BadRequest(
                    "The 'items' array could not be deserialized.",
                    httpContext.Request.Path));
        }

        var results = new BatchItemResult?[items.Count];

        // Phase 1: Validate all items and run pre-persist hooks.
        var validItems = new List<(int Index, TKey Id, TEntity Entity)>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                results[i] = new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} could not be deserialized.",
                        httpContext.Request.Path)
                };
                continue;
            }

            // Deserialize the body as the entity type
            TEntity? entity;
            try
            {
                entity = item.Body.Deserialize<TEntity>(jsonOptions);
            }
            catch (JsonException)
            {
                results[i] = new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} has an invalid body.",
                        httpContext.Request.Path)
                };
                continue;
            }

            if (entity is null)
            {
                results[i] = new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} body deserialized to null.",
                        httpContext.Request.Path)
                };
                continue;
            }

            // Fetch existing entity
            var existing = await repository.GetByIdAsync(item.Id, ct);
            if (existing is null)
            {
                var entityName = typeof(TEntity).Name;
                results[i] = new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status404NotFound,
                    Error = ProblemDetailsFactory.NotFound(entityName, item.Id!, httpContext.Request.Path)
                };
                continue;
            }

            // Validation
            if (options.EnableValidation)
            {
                var validationResult = EntityValidator.Validate(entity, jsonOptions.PropertyNamingPolicy);
                if (!validationResult.IsValid)
                {
                    results[i] = new BatchItemResult
                    {
                        Index = i,
                        Status = StatusCodes.Status400BadRequest,
                        Error = ProblemDetailsFactory.ValidationFailed(
                            new Dictionary<string, string[]>(validationResult.Errors),
                            httpContext.Request.Path)
                    };
                    continue;
                }
            }

            // Hooks
            if (pipeline is not null)
            {
                var hookContext = pipeline.CreateContext(
                    httpContext, RestLibOperation.BatchUpdate,
                    resourceId: item.Id, entity: entity, originalEntity: existing);

                var received = await pipeline.ExecuteOnRequestReceivedAsync(hookContext);
                if (!received)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }

                var validated = await pipeline.ExecuteOnRequestValidatedAsync(hookContext);
                if (!validated)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }

                var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
                if (!before)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }

                entity = hookContext.Entity ?? entity;
            }

            validItems.Add((i, item.Id, entity));
        }

        // Phase 2 & 3: Persist and run AfterPersist hooks.
        if (batchRepository is not null && validItems.Count > 0)
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
    /// <param name="config">The endpoint configuration.</param>
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
        RestLibEndpointConfiguration<TEntity, TKey> config,
        HookPipeline<TEntity, TKey>? pipeline,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
    {
        var items = DeserializeArray<BatchUpdateItem<TKey>>(itemsElement, jsonOptions);
        if (items is null)
        {
            return SingleErrorResponse(0, StatusCodes.Status400BadRequest,
                ProblemDetailsFactory.BadRequest(
                    "The 'items' array could not be deserialized.",
                    httpContext.Request.Path));
        }

        var results = new BatchItemResult?[items.Count];

        // Phase 1: Validate items (existence check + hooks).
        var validItems = new List<(int Index, TKey Id, JsonElement Body)>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                results[i] = new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} could not be deserialized.",
                        httpContext.Request.Path)
                };
                continue;
            }

            // Fetch existing entity (needed for 404 check and hook context)
            var existing = await repository.GetByIdAsync(item.Id, ct);
            if (existing is null)
            {
                var entityName = typeof(TEntity).Name;
                results[i] = new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status404NotFound,
                    Error = ProblemDetailsFactory.NotFound(entityName, item.Id!, httpContext.Request.Path)
                };
                continue;
            }

            // Hooks: OnRequestReceived, OnRequestValidated, BeforePersist
            if (pipeline is not null)
            {
                var hookContext = pipeline.CreateContext(
                    httpContext, RestLibOperation.BatchPatch,
                    resourceId: item.Id, entity: existing, originalEntity: existing);

                var received = await pipeline.ExecuteOnRequestReceivedAsync(hookContext);
                if (!received)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }

                var validated = await pipeline.ExecuteOnRequestValidatedAsync(hookContext);
                if (!validated)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }

                var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
                if (!before)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }
            }

            validItems.Add((i, item.Id, item.Body));
        }

        // Phase 2 & 3: Persist and run AfterPersist hooks.
        if (batchRepository is not null && validItems.Count > 0)
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
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The batch response with per-item results.</returns>
    internal static async Task<BatchResponse> ProcessDeleteAsync<TEntity, TKey>(
        JsonElement itemsElement,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        IBatchRepository<TEntity, TKey>? batchRepository,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        HookPipeline<TEntity, TKey>? pipeline,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
    {
        var keys = DeserializeArray<TKey>(itemsElement, jsonOptions);
        if (keys is null)
        {
            return SingleErrorResponse(0, StatusCodes.Status400BadRequest,
                ProblemDetailsFactory.BadRequest(
                    "The 'items' array could not be deserialized as a list of IDs.",
                    httpContext.Request.Path));
        }

        var results = new BatchItemResult?[keys.Count];

        // Phase 1: Validate keys and run pre-persist hooks.
        var validKeys = new List<(int Index, TKey Key)>();

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (key is null)
            {
                results[i] = new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} has a null or invalid ID.",
                        httpContext.Request.Path)
                };
                continue;
            }

            // Hooks: OnRequestReceived, OnRequestValidated, BeforePersist
            if (pipeline is not null)
            {
                var hookContext = pipeline.CreateContext(
                    httpContext, RestLibOperation.BatchDelete, resourceId: key);

                var received = await pipeline.ExecuteOnRequestReceivedAsync(hookContext);
                if (!received)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }

                var validated = await pipeline.ExecuteOnRequestValidatedAsync(hookContext);
                if (!validated)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }

                var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
                if (!before)
                {
                    results[i] = HookShortCircuitResult(i, hookContext);
                    continue;
                }
            }

            validKeys.Add((i, key));
        }

        // Phase 2 & 3: Persist and run AfterPersist hooks.
        if (batchRepository is not null && validKeys.Count > 0)
        {
            try
            {
                var keysToDelete = validKeys.Select(v => v.Key).ToList();
                var deletedCount = await batchRepository.DeleteManyAsync(keysToDelete, ct);

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

        return new BatchResponse { Items = results.ToList()! };
    }

    /// <summary>
    /// Deserializes a JSON array element into a list of items.
    /// </summary>
    private static IReadOnlyList<T?>? DeserializeArray<T>(
        JsonElement element,
        JsonSerializerOptions jsonOptions)
    {
        try
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return element.Deserialize<List<T?>>(jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a batch response with a single error entry.
    /// </summary>
    private static BatchResponse SingleErrorResponse(
        int index,
        int status,
        RestLibProblemDetails error)
    {
        return new BatchResponse
        {
            Items = [new BatchItemResult { Index = index, Status = status, Error = error }]
        };
    }

    /// <summary>
    /// Creates a batch item result from a hook short-circuit.
    /// When the hook provides an <see cref="HookContext{TEntity,TKey}.EarlyResult"/>,
    /// the status code and problem details are extracted from it.
    /// When no early result is set, falls back to 500 Internal Server Error,
    /// consistent with the single-item endpoint behaviour.
    /// </summary>
    private static BatchItemResult HookShortCircuitResult<TEntity, TKey>(
        int index,
        HookContext<TEntity, TKey> hookContext)
        where TEntity : class
    {
        if (hookContext.EarlyResult is null)
        {
            return new BatchItemResult
            {
                Index = index,
                Status = StatusCodes.Status500InternalServerError,
                Error = ProblemDetailsFactory.InternalError(
                    detail: "The operation was short-circuited by a hook.")
            };
        }

        var statusCode = hookContext.EarlyResult is IStatusCodeHttpResult statusResult
            ? statusResult.StatusCode ?? StatusCodes.Status500InternalServerError
            : StatusCodes.Status500InternalServerError;

        var error = hookContext.EarlyResult is IValueHttpResult { Value: RestLibProblemDetails problem }
            ? problem
            : ProblemDetailsFactory.HookShortCircuit(statusCode);

        return new BatchItemResult
        {
            Index = index,
            Status = statusCode,
            Error = error
        };
    }

    /// <summary>
    /// Creates a batch item result from an exception.
    /// </summary>
    private static BatchItemResult ExceptionResult(
        int index,
        Exception ex,
        string? instance)
    {
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status500InternalServerError,
            Error = ProblemDetailsFactory.InternalError(instance: instance)
        };
    }
}
