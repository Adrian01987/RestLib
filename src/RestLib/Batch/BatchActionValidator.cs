using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Responses;
using RestLib.Validation;

namespace RestLib.Batch;

/// <summary>
/// Handles per-item deserialization, validation, existence checks, and
/// pre-persist hook execution for batch operations. Returns validation
/// results without performing persistence.
/// </summary>
internal static class BatchActionValidator
{
    /// <summary>
    /// Deserializes a JSON array element into a list of items.
    /// Returns <c>null</c> if the element is not a JSON array or cannot be deserialized.
    /// </summary>
    /// <typeparam name="T">The type to deserialize each element as.</typeparam>
    /// <param name="element">The raw JSON element to deserialize.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <returns>A list of deserialized items, or <c>null</c> on failure.</returns>
    internal static IReadOnlyList<T?>? DeserializeArray<T>(
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
    /// Used when the entire items array cannot be deserialized.
    /// </summary>
    /// <param name="index">The item index for the error.</param>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="error">The problem details describing the error.</param>
    /// <returns>A batch response containing a single error item.</returns>
    internal static BatchResponse SingleErrorResponse(
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
    /// Validates a single create item: null check, data annotation validation,
    /// and pre-persist hook execution (OnRequestReceived, OnRequestValidated, BeforePersist).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="index">The item index within the batch.</param>
    /// <param name="entity">The deserialized entity, or <c>null</c>.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="options">The global RestLib options.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <returns>
    /// A tuple where <c>Error</c> is the validation error (or <c>null</c> if valid)
    /// and <c>Entity</c> is the (possibly hook-modified) entity to persist.
    /// </returns>
    internal static async Task<(BatchItemResult? Error, TEntity? Entity)> ValidateCreateItemAsync<TEntity, TKey>(
        int index,
        TEntity? entity,
        HttpContext httpContext,
        HookPipeline<TEntity, TKey>? pipeline,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions)
        where TEntity : class
        where TKey : notnull
    {
        if (entity is null)
        {
            return (BadRequestResult(index, $"Item at index {index} could not be deserialized.", httpContext.Request.Path), null);
        }

        // Validation
        if (options.EnableValidation)
        {
            var validationResult = EntityValidator.Validate(entity, jsonOptions.PropertyNamingPolicy);
            if (!validationResult.IsValid)
            {
                return (ValidationFailedResult(index, validationResult, httpContext.Request.Path), null);
            }
        }

        // Hooks: OnRequestReceived, OnRequestValidated, BeforePersist
        if (pipeline is not null)
        {
            var hookContext = pipeline.CreateContext(
                httpContext, RestLibOperation.BatchCreate, entity: entity);

            var hookError = await RunPrePersistHooksAsync(index, pipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, null);
            }

            entity = hookContext.Entity ?? entity;
        }

        return (null, entity);
    }

    /// <summary>
    /// Validates a single update item: null check, body deserialization,
    /// existence check, data annotation validation, and pre-persist hook execution.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="index">The item index within the batch.</param>
    /// <param name="item">The deserialized batch update item, or <c>null</c>.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository for existence checks.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="options">The global RestLib options.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple where <c>Error</c> is the validation error (or <c>null</c> if valid),
    /// <c>Id</c> is the entity key, and <c>Entity</c> is the (possibly hook-modified) entity.
    /// </returns>
    internal static async Task<(BatchItemResult? Error, TKey? Id, TEntity? Entity)> ValidateUpdateItemAsync<TEntity, TKey>(
        int index,
        BatchUpdateItem<TKey>? item,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        HookPipeline<TEntity, TKey>? pipeline,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
        where TKey : notnull
    {
        if (item is null)
        {
            return (BadRequestResult(index, $"Item at index {index} could not be deserialized.", httpContext.Request.Path), default, null);
        }

        // Deserialize the body as the entity type
        TEntity? entity;
        try
        {
            entity = item.Body.Deserialize<TEntity>(jsonOptions);
        }
        catch (JsonException)
        {
            return (BadRequestResult(index, $"Item at index {index} has an invalid body.", httpContext.Request.Path), default, null);
        }

        if (entity is null)
        {
            return (BadRequestResult(index, $"Item at index {index} body deserialized to null.", httpContext.Request.Path), default, null);
        }

        // Fetch existing entity
        var existing = await repository.GetByIdAsync(item.Id, ct);
        if (existing is null)
        {
            var entityName = typeof(TEntity).Name;
            return (NotFoundResult(index, entityName, item.Id!, httpContext.Request.Path), default, null);
        }

        // Validation
        if (options.EnableValidation)
        {
            var validationResult = EntityValidator.Validate(entity, jsonOptions.PropertyNamingPolicy);
            if (!validationResult.IsValid)
            {
                return (ValidationFailedResult(index, validationResult, httpContext.Request.Path), default, null);
            }
        }

        // Hooks
        if (pipeline is not null)
        {
            var hookContext = pipeline.CreateContext(
                httpContext, RestLibOperation.BatchUpdate,
                resourceId: item.Id, entity: entity, originalEntity: existing);

            var hookError = await RunPrePersistHooksAsync(index, pipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, default, null);
            }

            entity = hookContext.Entity ?? entity;
        }

        return (null, item.Id, entity);
    }

    /// <summary>
    /// Validates a single patch item: null check, existence check,
    /// and pre-persist hook execution. Data annotation validation for
    /// patches is deferred to post-persist since the merged entity is
    /// only available after patching.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="index">The item index within the batch.</param>
    /// <param name="item">The deserialized batch update item, or <c>null</c>.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The entity repository for existence checks.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple where <c>Error</c> is the validation error (or <c>null</c> if valid),
    /// <c>Id</c> is the entity key, and <c>Body</c> is the raw patch document.
    /// </returns>
    internal static async Task<(BatchItemResult? Error, TKey? Id, JsonElement Body)> ValidatePatchItemAsync<TEntity, TKey>(
        int index,
        BatchUpdateItem<TKey>? item,
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        HookPipeline<TEntity, TKey>? pipeline,
        CancellationToken ct)
        where TEntity : class
        where TKey : notnull
    {
        if (item is null)
        {
            return (BadRequestResult(index, $"Item at index {index} could not be deserialized.", httpContext.Request.Path), default, default);
        }

        // Fetch existing entity (needed for 404 check and hook context)
        var existing = await repository.GetByIdAsync(item.Id, ct);
        if (existing is null)
        {
            var entityName = typeof(TEntity).Name;
            return (NotFoundResult(index, entityName, item.Id!, httpContext.Request.Path), default, default);
        }

        // Hooks: OnRequestReceived, OnRequestValidated, BeforePersist
        if (pipeline is not null)
        {
            var hookContext = pipeline.CreateContext(
                httpContext, RestLibOperation.BatchPatch,
                resourceId: item.Id, entity: existing, originalEntity: existing);

            var hookError = await RunPrePersistHooksAsync(index, pipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, default, default);
            }
        }

        return (null, item.Id, item.Body);
    }

    /// <summary>
    /// Validates a single delete item: null check and pre-persist hook execution.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="index">The item index within the batch.</param>
    /// <param name="key">The deserialized key, or <c>null</c>.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="pipeline">The optional hook pipeline.</param>
    /// <returns>
    /// A tuple where <c>Error</c> is the validation error (or <c>null</c> if valid)
    /// and <c>Key</c> is the validated key.
    /// </returns>
    internal static async Task<(BatchItemResult? Error, TKey? Key)> ValidateDeleteItemAsync<TEntity, TKey>(
        int index,
        TKey? key,
        HttpContext httpContext,
        HookPipeline<TEntity, TKey>? pipeline)
        where TEntity : class
        where TKey : notnull
    {
        if (key is null)
        {
            return (BadRequestResult(index, $"Item at index {index} has a null or invalid ID.", httpContext.Request.Path), default);
        }

        // Hooks: OnRequestReceived, OnRequestValidated, BeforePersist
        if (pipeline is not null)
        {
            var hookContext = pipeline.CreateContext(
                httpContext, RestLibOperation.BatchDelete, resourceId: key);

            var hookError = await RunPrePersistHooksAsync(index, pipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, default);
            }
        }

        return (null, key);
    }

    /// <summary>
    /// Creates a batch item result from a hook short-circuit.
    /// When the hook provides an <see cref="HookContext{TEntity,TKey}.EarlyResult"/>,
    /// the status code and problem details are extracted from it.
    /// When no early result is set, falls back to 500 Internal Server Error,
    /// consistent with the single-item endpoint behaviour.
    /// </summary>
    internal static BatchItemResult HookShortCircuitResult<TEntity, TKey>(
        int index,
        HookContext<TEntity, TKey> hookContext)
        where TEntity : class
        where TKey : notnull
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
    /// Runs the three pre-persist hook stages (OnRequestReceived, OnRequestValidated,
    /// BeforePersist) and returns an error result if any stage short-circuits.
    /// </summary>
    private static async Task<BatchItemResult?> RunPrePersistHooksAsync<TEntity, TKey>(
        int index,
        HookPipeline<TEntity, TKey> pipeline,
        HookContext<TEntity, TKey> hookContext)
        where TEntity : class
        where TKey : notnull
    {
        var received = await pipeline.ExecuteOnRequestReceivedAsync(hookContext);
        if (!received)
        {
            return HookShortCircuitResult(index, hookContext);
        }

        var validated = await pipeline.ExecuteOnRequestValidatedAsync(hookContext);
        if (!validated)
        {
            return HookShortCircuitResult(index, hookContext);
        }

        var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
        if (!before)
        {
            return HookShortCircuitResult(index, hookContext);
        }

        return null;
    }

    /// <summary>
    /// Creates a 400 Bad Request batch item result.
    /// </summary>
    private static BatchItemResult BadRequestResult(int index, string detail, string? instance)
    {
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status400BadRequest,
            Error = ProblemDetailsFactory.BadRequest(detail, instance)
        };
    }

    /// <summary>
    /// Creates a 404 Not Found batch item result.
    /// </summary>
    private static BatchItemResult NotFoundResult<TKey>(int index, string entityName, TKey id, string? instance)
    {
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status404NotFound,
            Error = ProblemDetailsFactory.NotFound(entityName, id!, instance)
        };
    }

    /// <summary>
    /// Creates a 400 Validation Failed batch item result.
    /// </summary>
    private static BatchItemResult ValidationFailedResult(
        int index,
        EntityValidationResult validationResult,
        string? instance)
    {
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status400BadRequest,
            Error = ProblemDetailsFactory.ValidationFailed(
                new Dictionary<string, string[]>(validationResult.Errors),
                instance)
        };
    }
}
