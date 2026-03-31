using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Responses;
using RestLib.Validation;

namespace RestLib.Batch;

/// <summary>
/// Orchestrates the processing of batch requests, handling per-item
/// validation, hooks, and repository operations.
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

        var results = new List<BatchItemResult>(items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            var entity = items[i];
            if (entity is null)
            {
                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} could not be deserialized.",
                        httpContext.Request.Path)
                });
                continue;
            }

            // Validation
            if (options.EnableValidation)
            {
                var validationResult = EntityValidator.Validate(entity, jsonOptions.PropertyNamingPolicy);
                if (!validationResult.IsValid)
                {
                    results.Add(new BatchItemResult
                    {
                        Index = i,
                        Status = StatusCodes.Status400BadRequest,
                        Error = ProblemDetailsFactory.ValidationFailed(
                            new Dictionary<string, string[]>(validationResult.Errors),
                            httpContext.Request.Path)
                    });
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
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                var validated = await pipeline.ExecuteOnRequestValidatedAsync(hookContext);
                if (!validated)
                {
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
                if (!before)
                {
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                entity = hookContext.Entity ?? entity;
            }

            // Persist
            try
            {
                var created = await repository.CreateAsync(entity, ct);

                // Hook: AfterPersist
                if (pipeline is not null)
                {
                    var afterContext = pipeline.CreateContext(
                        httpContext, RestLibOperation.BatchCreate, entity: created);
                    await pipeline.ExecuteAfterPersistAsync(afterContext);
                }

                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status201Created,
                    Entity = created
                });
            }
            catch (Exception ex)
            {
                results.Add(ExceptionResult(i, ex, httpContext.Request.Path));
            }
        }

        return new BatchResponse { Items = results };
    }

    /// <summary>
    /// Processes a batch update request (full replacement).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="itemsElement">The raw JSON items array.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
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

        var results = new List<BatchItemResult>(items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} could not be deserialized.",
                        httpContext.Request.Path)
                });
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
                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} has an invalid body.",
                        httpContext.Request.Path)
                });
                continue;
            }

            if (entity is null)
            {
                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} body deserialized to null.",
                        httpContext.Request.Path)
                });
                continue;
            }

            // Fetch existing entity
            var existing = await repository.GetByIdAsync(item.Id, ct);
            if (existing is null)
            {
                var entityName = typeof(TEntity).Name;
                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status404NotFound,
                    Error = ProblemDetailsFactory.NotFound(entityName, item.Id!, httpContext.Request.Path)
                });
                continue;
            }

            // Validation
            if (options.EnableValidation)
            {
                var validationResult = EntityValidator.Validate(entity, jsonOptions.PropertyNamingPolicy);
                if (!validationResult.IsValid)
                {
                    results.Add(new BatchItemResult
                    {
                        Index = i,
                        Status = StatusCodes.Status400BadRequest,
                        Error = ProblemDetailsFactory.ValidationFailed(
                            new Dictionary<string, string[]>(validationResult.Errors),
                            httpContext.Request.Path)
                    });
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
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                var validated = await pipeline.ExecuteOnRequestValidatedAsync(hookContext);
                if (!validated)
                {
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
                if (!before)
                {
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                entity = hookContext.Entity ?? entity;
            }

            // Persist
            try
            {
                var updated = await repository.UpdateAsync(item.Id, entity, ct);
                if (updated is null)
                {
                    var entityName = typeof(TEntity).Name;
                    results.Add(new BatchItemResult
                    {
                        Index = i,
                        Status = StatusCodes.Status404NotFound,
                        Error = ProblemDetailsFactory.NotFound(entityName, item.Id!, httpContext.Request.Path)
                    });
                    continue;
                }

                // Hook: AfterPersist
                if (pipeline is not null)
                {
                    var afterContext = pipeline.CreateContext(
                        httpContext, RestLibOperation.BatchUpdate,
                        resourceId: item.Id, entity: updated);
                    await pipeline.ExecuteAfterPersistAsync(afterContext);
                }

                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status200OK,
                    Entity = updated
                });
            }
            catch (Exception ex)
            {
                results.Add(ExceptionResult(i, ex, httpContext.Request.Path));
            }
        }

        return new BatchResponse { Items = results };
    }

    /// <summary>
    /// Processes a batch patch request (partial update).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="itemsElement">The raw JSON items array.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
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

        var results = new List<BatchItemResult>(items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} could not be deserialized.",
                        httpContext.Request.Path)
                });
                continue;
            }

            // Fetch existing entity
            var existing = await repository.GetByIdAsync(item.Id, ct);
            if (existing is null)
            {
                var entityName = typeof(TEntity).Name;
                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status404NotFound,
                    Error = ProblemDetailsFactory.NotFound(entityName, item.Id!, httpContext.Request.Path)
                });
                continue;
            }

            // Merge: serialize existing -> overlay patch body -> deserialize back
            TEntity? patched;
            try
            {
                var existingJson = JsonSerializer.Serialize(existing, jsonOptions);
                var existingNode = JsonNode.Parse(existingJson);
                var patchNode = JsonNode.Parse(item.Body.GetRawText());

                if (existingNode is JsonObject existingObj
                    && patchNode is JsonObject patchObj)
                {
                    foreach (var prop in patchObj)
                    {
                        existingObj[prop.Key] = prop.Value?.DeepClone();
                    }
                }

                patched = existingNode?.Deserialize<TEntity>(jsonOptions);
            }
            catch (JsonException)
            {
                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} has an invalid patch body.",
                        httpContext.Request.Path)
                });
                continue;
            }

            if (patched is null)
            {
                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} patch produced a null entity.",
                        httpContext.Request.Path)
                });
                continue;
            }

            // Validation
            if (options.EnableValidation)
            {
                var validationResult = EntityValidator.Validate(patched, jsonOptions.PropertyNamingPolicy);
                if (!validationResult.IsValid)
                {
                    results.Add(new BatchItemResult
                    {
                        Index = i,
                        Status = StatusCodes.Status400BadRequest,
                        Error = ProblemDetailsFactory.ValidationFailed(
                            new Dictionary<string, string[]>(validationResult.Errors),
                            httpContext.Request.Path)
                    });
                    continue;
                }
            }

            // Hooks
            if (pipeline is not null)
            {
                var hookContext = pipeline.CreateContext(
                    httpContext, RestLibOperation.BatchPatch,
                    resourceId: item.Id, entity: patched, originalEntity: existing);

                var received = await pipeline.ExecuteOnRequestReceivedAsync(hookContext);
                if (!received)
                {
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                var validated = await pipeline.ExecuteOnRequestValidatedAsync(hookContext);
                if (!validated)
                {
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
                if (!before)
                {
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                patched = hookContext.Entity ?? patched;
            }

            // Persist
            try
            {
                var updated = await repository.UpdateAsync(item.Id, patched, ct);
                if (updated is null)
                {
                    var entityName = typeof(TEntity).Name;
                    results.Add(new BatchItemResult
                    {
                        Index = i,
                        Status = StatusCodes.Status404NotFound,
                        Error = ProblemDetailsFactory.NotFound(entityName, item.Id!, httpContext.Request.Path)
                    });
                    continue;
                }

                // Hook: AfterPersist
                if (pipeline is not null)
                {
                    var afterContext = pipeline.CreateContext(
                        httpContext, RestLibOperation.BatchPatch,
                        resourceId: item.Id, entity: updated);
                    await pipeline.ExecuteAfterPersistAsync(afterContext);
                }

                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status200OK,
                    Entity = updated
                });
            }
            catch (Exception ex)
            {
                results.Add(ExceptionResult(i, ex, httpContext.Request.Path));
            }
        }

        return new BatchResponse { Items = results };
    }

    /// <summary>
    /// Processes a batch delete request.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="itemsElement">The raw JSON items array.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The batch response with per-item results.</returns>
    internal static async Task<BatchResponse> ProcessDeleteAsync<TEntity, TKey>(
        JsonElement itemsElement,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
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

        var results = new List<BatchItemResult>(keys.Count);

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (key is null)
            {
                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status400BadRequest,
                    Error = ProblemDetailsFactory.BadRequest(
                        $"Item at index {i} has a null or invalid ID.",
                        httpContext.Request.Path)
                });
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
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                var validated = await pipeline.ExecuteOnRequestValidatedAsync(hookContext);
                if (!validated)
                {
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }

                var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
                if (!before)
                {
                    results.Add(HookShortCircuitResult(i, hookContext));
                    continue;
                }
            }

            // Persist
            try
            {
                var deleted = await repository.DeleteAsync(key, ct);
                if (!deleted)
                {
                    var entityName = typeof(TEntity).Name;
                    results.Add(new BatchItemResult
                    {
                        Index = i,
                        Status = StatusCodes.Status404NotFound,
                        Error = ProblemDetailsFactory.NotFound(entityName, key, httpContext.Request.Path)
                    });
                    continue;
                }

                // Hook: AfterPersist
                if (pipeline is not null)
                {
                    var afterContext = pipeline.CreateContext(
                        httpContext, RestLibOperation.BatchDelete, resourceId: key);
                    await pipeline.ExecuteAfterPersistAsync(afterContext);
                }

                results.Add(new BatchItemResult
                {
                    Index = i,
                    Status = StatusCodes.Status204NoContent
                });
            }
            catch (Exception ex)
            {
                results.Add(ExceptionResult(i, ex, httpContext.Request.Path));
            }
        }

        return new BatchResponse { Items = results };
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
    /// </summary>
    private static BatchItemResult HookShortCircuitResult<TEntity, TKey>(
        int index,
        HookContext<TEntity, TKey> hookContext)
        where TEntity : class
    {
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status400BadRequest,
            Error = ProblemDetailsFactory.BadRequest(
                "The operation was short-circuited by a hook.")
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
