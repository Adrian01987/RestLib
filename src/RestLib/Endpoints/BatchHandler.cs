using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Internal;
using RestLib.Logging;
using RestLib.Mapping;

namespace RestLib.Endpoints;

/// <summary>
/// Handles POST requests for batch operations.
/// </summary>
internal static class BatchHandler
{
    /// <summary>
    /// Creates the delegate for the Batch endpoint.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<HttpContext, Task<IResult>>
        CreateDelegate<TEntity, TKey>(RestLibEndpointConfiguration<TEntity, TKey> config)
        where TEntity : class
        where TKey : notnull
    {
        return async httpContext =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Batch");
            var repository = httpContext.RequestServices.GetRequiredService<IRepository<TEntity, TKey>>();
            var ct = httpContext.RequestAborted;
            var instance = httpContext.Request.Path.ToString();

            // Deserialize the envelope
            BatchRequestEnvelope? envelope;
            try
            {
                envelope = await httpContext.Request.ReadFromJsonAsync<BatchRequestEnvelope>(jsonOptions, ct);
            }
            catch (JsonException ex)
            {
                RestLibLogMessages.BatchEnvelopeDeserializationFailed(logger, ex);
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The request body is not valid JSON.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            if (envelope is null)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The request body is empty.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            // Validate action
            if (!Enum.TryParse<BatchAction>(envelope.Action, ignoreCase: true, out var action))
            {
                var allowedActions = config.EnabledBatchActions
                    .Select(a => a.ToString().ToLowerInvariant());
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    $"'{envelope.Action}' is not a valid batch action.",
                    errors: new Dictionary<string, string[]>
                    {
                        ["action"] = [$"'{envelope.Action}' is not a valid batch action. Allowed actions: {string.Join(", ", allowedActions)}."]
                    },
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            // Verify action is enabled
            if (!config.IsBatchActionEnabled(action))
            {
                var enabledActions = config.EnabledBatchActions
                    .Select(a => a.ToString().ToLowerInvariant());
                return Responses.ProblemDetailsResult.BatchActionNotEnabled(
                    action.ToString().ToLowerInvariant(),
                    enabledActions,
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            // Validate items array exists
            if (envelope.Items.ValueKind == JsonValueKind.Undefined
                || envelope.Items.ValueKind == JsonValueKind.Null)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' array is required.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            if (envelope.Items.ValueKind != JsonValueKind.Array)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' property must be an array.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            var itemCount = envelope.Items.GetArrayLength();

            // Validate non-empty
            if (itemCount == 0)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' array must contain at least one item.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            // Validate batch size
            if (options.MaxBatchSize > 0 && itemCount > options.MaxBatchSize)
            {
                return Responses.ProblemDetailsResult.BatchSizeExceeded(
                    itemCount,
                    options.MaxBatchSize,
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            // Create hook pipeline if hooks are configured
            var pipeline = config.Hooks is not null ? new HookPipeline<TEntity, TKey>(config.Hooks, logger) : null;

            var actionName = action.ToString().ToLowerInvariant();
            RestLibLogMessages.BatchRequestReceived(logger, actionName, itemCount);

            // Resolve optional batch repository for bulk-optimized operations
            var batchRepository = httpContext.RequestServices
                .GetService<IBatchRepository<TEntity, TKey>>();

            // Build the shared batch context
            var batchContext = new BatchContext<TEntity, TKey>
            {
                HttpContext = httpContext,
                Repository = repository,
                BatchRepository = batchRepository,
                Pipeline = pipeline,
                Options = options,
                JsonOptions = jsonOptions,
                CancellationToken = ct,
                EndpointConfig = config,
                CollectionPath = GetCollectionPathFromBatchPath(instance),
                Logger = logger
            };

            BatchResponse response;
            try
            {
                // Dispatch to the appropriate pipeline
                response = action switch
                {
                    BatchAction.Create => await new BatchCreatePipeline<TEntity, TKey>()
                        .ProcessAsync(envelope.Items, batchContext),
                    BatchAction.Update => await new BatchUpdatePipeline<TEntity, TKey>()
                        .ProcessAsync(ParseUpdateItems(envelope.Items, config.KeyRouteParts, jsonOptions), batchContext),
                    BatchAction.Patch => await new BatchPatchPipeline<TEntity, TKey>()
                        .ProcessAsync(ParseUpdateItems(envelope.Items, config.KeyRouteParts, jsonOptions), batchContext),
                    BatchAction.Delete => await new BatchDeletePipeline<TEntity, TKey>()
                        .ProcessAsync(ParseDeleteItems(envelope.Items, config.KeyRouteParts, jsonOptions), batchContext),
                    _ => throw new InvalidOperationException($"Unexpected batch action: {action}")
                };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    ex.Message,
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            // BeforeResponse hook — runs once for the entire batch, after all items are processed.
            if (pipeline is not null)
            {
                var batchOperation = action switch
                {
                    BatchAction.Create => RestLibOperation.BatchCreate,
                    BatchAction.Update => RestLibOperation.BatchUpdate,
                    BatchAction.Patch => RestLibOperation.BatchPatch,
                    BatchAction.Delete => RestLibOperation.BatchDelete,
                    _ => RestLibOperation.BatchCreate
                };
                var hookContext = pipeline.CreateContext(httpContext, batchOperation);
                var beforeResponseResult = await HookHelper.ExecuteHookAsync(
                    pipeline.ExecuteBeforeResponseAsync, hookContext);
                if (beforeResponseResult is not null) return beforeResponseResult;
            }

            // Determine response status code
            var allSucceeded = response.Items.All(r => r.Status is >= 200 and < 300);
            var statusCode = allSucceeded
                ? StatusCodes.Status200OK
                : StatusCodes.Status207MultiStatus;

            var succeeded = response.Items.Count(r => r.Status is >= 200 and < 300);
            var failed = response.Items.Count - succeeded;
            RestLibLogMessages.BatchCompleted(logger, actionName, response.Items.Count, succeeded, failed, statusCode);

            return Results.Json(response, jsonOptions, statusCode: statusCode);
        };
    }

    /// <summary>
    /// Creates the delegate for a mapped batch endpoint.
    /// </summary>
    /// <typeparam name="TApiModel">The API model type.</typeparam>
    /// <typeparam name="TDbModel">The DB model type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<HttpContext, Task<IResult>>
        CreateMappedDelegate<TApiModel, TDbModel, TKey>(RestLibEndpointConfiguration<TApiModel, TDbModel, TKey> config)
        where TApiModel : class
        where TDbModel : class
        where TKey : notnull
    {
        return async httpContext =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Batch");
            var repository = httpContext.RequestServices.GetRequiredService<IRepository<TDbModel, TKey>>();
            var mapper = RestLibMapperResolver.Resolve<TApiModel, TDbModel>(
                httpContext.RequestServices,
                config.MapperName,
                config.UseAutoMapper,
                config.ResourceName);
            var ct = httpContext.RequestAborted;
            var instance = httpContext.Request.Path.ToString();

            BatchRequestEnvelope? envelope;
            try
            {
                envelope = await httpContext.Request.ReadFromJsonAsync<BatchRequestEnvelope>(jsonOptions, ct);
            }
            catch (JsonException ex)
            {
                RestLibLogMessages.BatchEnvelopeDeserializationFailed(logger, ex);
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The request body is not valid JSON.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            if (envelope is null)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The request body is empty.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            if (!Enum.TryParse<BatchAction>(envelope.Action, ignoreCase: true, out var action))
            {
                var allowedActions = config.EnabledBatchActions
                    .Select(a => a.ToString().ToLowerInvariant());
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    $"'{envelope.Action}' is not a valid batch action.",
                    errors: new Dictionary<string, string[]>
                    {
                        ["action"] = [$"'{envelope.Action}' is not a valid batch action. Allowed actions: {string.Join(", ", allowedActions)}."]
                    },
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            if (!config.IsBatchActionEnabled(action))
            {
                var enabledActions = config.EnabledBatchActions
                    .Select(a => a.ToString().ToLowerInvariant());
                return Responses.ProblemDetailsResult.BatchActionNotEnabled(
                    action.ToString().ToLowerInvariant(),
                    enabledActions,
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            if (envelope.Items.ValueKind == JsonValueKind.Undefined
                || envelope.Items.ValueKind == JsonValueKind.Null)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' array is required.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            if (envelope.Items.ValueKind != JsonValueKind.Array)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' property must be an array.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            var itemCount = envelope.Items.GetArrayLength();
            if (itemCount == 0)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' array must contain at least one item.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            if (options.MaxBatchSize > 0 && itemCount > options.MaxBatchSize)
            {
                return Responses.ProblemDetailsResult.BatchSizeExceeded(
                    itemCount,
                    options.MaxBatchSize,
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            var apiPipeline = config.UsesDbModelHooks || config.Hooks is null
                ? null
                : new HookPipeline<TApiModel, TKey>(config.Hooks, logger);
            var dbPipeline = config.UsesDbModelHooks && config.DbModelHooks is not null
                ? new HookPipeline<TDbModel, TKey>(config.DbModelHooks, logger)
                : null;

            var actionName = action.ToString().ToLowerInvariant();
            RestLibLogMessages.BatchRequestReceived(logger, actionName, itemCount);

            var batchRepository = httpContext.RequestServices.GetService<IBatchRepository<TDbModel, TKey>>();
            var batchContext = new MappedBatchContext<TApiModel, TDbModel, TKey>
            {
                HttpContext = httpContext,
                Repository = repository,
                BatchRepository = batchRepository,
                ApiPipeline = apiPipeline,
                DbPipeline = dbPipeline,
                Mapper = mapper,
                Options = options,
                JsonOptions = jsonOptions,
                CancellationToken = ct,
                EndpointConfig = config,
                CollectionPath = GetCollectionPathFromBatchPath(instance),
                Logger = logger
            };

            BatchResponse response;
            try
            {
                response = action switch
                {
                    BatchAction.Create => await new MappedBatchCreatePipeline<TApiModel, TDbModel, TKey>()
                        .ProcessAsync(envelope.Items, batchContext),
                    BatchAction.Update => await new MappedBatchUpdatePipeline<TApiModel, TDbModel, TKey>()
                        .ProcessAsync(ParseUpdateItems(envelope.Items, config.KeyRouteParts, jsonOptions), batchContext),
                    BatchAction.Patch => await new MappedBatchPatchPipeline<TApiModel, TDbModel, TKey>()
                        .ProcessAsync(ParseUpdateItems(envelope.Items, config.KeyRouteParts, jsonOptions), batchContext),
                    BatchAction.Delete => await new MappedBatchDeletePipeline<TApiModel, TDbModel, TKey>()
                        .ProcessAsync(ParseDeleteItems(envelope.Items, config.KeyRouteParts, jsonOptions), batchContext),
                    _ => throw new InvalidOperationException($"Unexpected batch action: {action}")
                };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    ex.Message,
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger,
                    options: options);
            }

            if (dbPipeline is not null)
            {
                var batchOperation = action switch
                {
                    BatchAction.Create => RestLibOperation.BatchCreate,
                    BatchAction.Update => RestLibOperation.BatchUpdate,
                    BatchAction.Patch => RestLibOperation.BatchPatch,
                    BatchAction.Delete => RestLibOperation.BatchDelete,
                    _ => RestLibOperation.BatchCreate
                };
                var hookContext = dbPipeline.CreateContext(httpContext, batchOperation);
                var beforeResponseResult = await HookHelper.ExecuteHookAsync(
                    dbPipeline.ExecuteBeforeResponseAsync,
                    hookContext);
                if (beforeResponseResult is not null)
                {
                    return beforeResponseResult;
                }
            }
            else if (apiPipeline is not null)
            {
                var batchOperation = action switch
                {
                    BatchAction.Create => RestLibOperation.BatchCreate,
                    BatchAction.Update => RestLibOperation.BatchUpdate,
                    BatchAction.Patch => RestLibOperation.BatchPatch,
                    BatchAction.Delete => RestLibOperation.BatchDelete,
                    _ => RestLibOperation.BatchCreate
                };
                var hookContext = apiPipeline.CreateContext(httpContext, batchOperation);
                var beforeResponseResult = await HookHelper.ExecuteHookAsync(
                    apiPipeline.ExecuteBeforeResponseAsync,
                    hookContext);
                if (beforeResponseResult is not null)
                {
                    return beforeResponseResult;
                }
            }

            var allSucceeded = response.Items.All(r => r.Status is >= 200 and < 300);
            var statusCode = allSucceeded
                ? StatusCodes.Status200OK
                : StatusCodes.Status207MultiStatus;

            var succeeded = response.Items.Count(r => r.Status is >= 200 and < 300);
            var failed = response.Items.Count - succeeded;
            RestLibLogMessages.BatchCompleted(logger, actionName, response.Items.Count, succeeded, failed, statusCode);

            return Results.Json(response, jsonOptions, statusCode: statusCode);
        };
    }

    /// <summary>
    /// Extracts the collection path from the batch endpoint path by removing the trailing "/batch" segment.
    /// For example, "/api/products/batch" becomes "/api/products".
    /// </summary>
    /// <param name="batchPath">The full batch endpoint path.</param>
    /// <returns>The collection path.</returns>
    private static string GetCollectionPathFromBatchPath(string batchPath)
    {
        const string batchSuffix = "/batch";
        if (batchPath.EndsWith(batchSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return batchPath[..^batchSuffix.Length];
        }

        return batchPath;
    }

    private static JsonElement ParseUpdateItems<TKey>(
        JsonElement itemsElement,
        IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts,
        JsonSerializerOptions jsonOptions)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keyRouteParts);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        if (keyRouteParts.Count <= 1)
        {
            return itemsElement;
        }

        if (itemsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The 'items' property must be an array.");
        }

        var parsedItems = itemsElement.EnumerateArray()
            .Select(item => ParseUpdateItem(item, keyRouteParts, jsonOptions))
            .ToList();

        return JsonSerializer.SerializeToElement(parsedItems, jsonOptions);
    }

    private static JsonElement ParseDeleteItems<TKey>(
        JsonElement itemsElement,
        IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts,
        JsonSerializerOptions jsonOptions)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keyRouteParts);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        if (keyRouteParts.Count <= 1)
        {
            return itemsElement;
        }

        if (itemsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The 'items' property must be an array.");
        }

        var parsedItems = itemsElement.EnumerateArray()
            .Select(item => ParseCompositeKey(item, keyRouteParts, jsonOptions))
            .ToList();

        return JsonSerializer.SerializeToElement(parsedItems, jsonOptions);
    }

    private static BatchUpdateItem<TKey> ParseUpdateItem<TKey>(
        JsonElement item,
        IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts,
        JsonSerializerOptions jsonOptions)
        where TKey : notnull
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Batch update and patch items must be objects with 'id' and 'body' properties.");
        }

        if (!TryGetObjectProperty(item, "id", out var idElement))
        {
            throw new JsonException("Batch update and patch items must include an 'id' property.");
        }

        if (!TryGetObjectProperty(item, "body", out var bodyElement))
        {
            throw new JsonException("Batch update and patch items must include a 'body' property.");
        }

        return new BatchUpdateItem<TKey>
        {
            Id = ParseCompositeKey(idElement, keyRouteParts, jsonOptions),
            Body = bodyElement.Clone()
        };
    }

    private static TKey ParseCompositeKey<TKey>(
        JsonElement keyElement,
        IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts,
        JsonSerializerOptions jsonOptions)
        where TKey : notnull
    {
        if (keyRouteParts.Count <= 1)
        {
            return (TKey)RestLibKeyConversion.DeserializeJsonValue(keyElement, typeof(TKey), jsonOptions);
        }

        if (keyElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Composite batch keys must be JSON objects.");
        }

        var keyType = typeof(TKey);
        if (!keyType.IsGenericType || keyType.GetGenericTypeDefinition() != typeof(RestLibCompositeKey<,>))
        {
            throw new JsonException(
                $"Composite batch keys require TKey '{typeof(TKey).Name}' to be RestLibCompositeKey<TFirst, TSecond>.");
        }

        var genericArguments = keyType.GetGenericArguments();
        var firstValue = GetCompositeKeyPartValue(keyElement, keyRouteParts[0], genericArguments[0], jsonOptions);
        var secondValue = GetCompositeKeyPartValue(keyElement, keyRouteParts[1], genericArguments[1], jsonOptions);
        var constructor = keyType.GetConstructor([genericArguments[0], genericArguments[1]])
            ?? throw new JsonException($"RestLib could not resolve the composite key constructor for '{keyType.Name}'.");

        return (TKey)constructor.Invoke([firstValue, secondValue]);
    }

    private static object GetCompositeKeyPartValue<TKey>(
        JsonElement keyElement,
        RestLibKeyRoutePart<TKey> keyRoutePart,
        Type targetType,
        JsonSerializerOptions jsonOptions)
        where TKey : notnull
    {
        foreach (var propertyName in GetCompositeKeyPropertyNames(keyRoutePart))
        {
            if (TryGetObjectProperty(keyElement, propertyName, out var valueElement))
            {
                return RestLibKeyConversion.DeserializeJsonValue(valueElement, targetType, jsonOptions);
            }
        }

        throw new JsonException(
            $"Composite batch key is missing required property '{JsonNamingPolicy.SnakeCaseLower.ConvertName(keyRoutePart.PropertyName)}'.");
    }

    private static IEnumerable<string> GetCompositeKeyPropertyNames<TKey>(RestLibKeyRoutePart<TKey> keyRoutePart)
        where TKey : notnull
    {
        yield return JsonNamingPolicy.SnakeCaseLower.ConvertName(keyRoutePart.PropertyName);
        yield return keyRoutePart.PropertyName;
        yield return keyRoutePart.RouteParameterName;
    }

    private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
