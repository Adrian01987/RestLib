using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;

namespace RestLib.Batch;

/// <summary>
/// Holds all shared services and state needed during batch processing,
/// avoiding repetitive parameter passing across pipeline methods.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class BatchContext<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Gets the current HTTP context.
    /// </summary>
    internal required HttpContext HttpContext { get; init; }

    /// <summary>
    /// Gets the entity repository.
    /// </summary>
    internal required IRepository<TEntity, TKey> Repository { get; init; }

    /// <summary>
    /// Gets the optional batch-optimized repository.
    /// </summary>
    internal IBatchRepository<TEntity, TKey>? BatchRepository { get; init; }

    /// <summary>
    /// Gets the optional hook pipeline.
    /// </summary>
    internal HookPipeline<TEntity, TKey>? Pipeline { get; init; }

    /// <summary>
    /// Gets the global RestLib options.
    /// </summary>
    internal required RestLibOptions Options { get; init; }

    /// <summary>
    /// Gets the JSON serializer options.
    /// </summary>
    internal required JsonSerializerOptions JsonOptions { get; init; }

    /// <summary>
    /// Gets the cancellation token.
    /// </summary>
    internal required CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Gets the logger for batch pipeline operations.
    /// </summary>
    internal required ILogger Logger { get; init; }

    /// <summary>
    /// Gets the endpoint configuration. Used for HATEOAS link generation
    /// to determine which operations are enabled.
    /// </summary>
    internal required RestLibEndpointConfiguration<TEntity, TKey> EndpointConfig { get; init; }

    /// <summary>
    /// Gets the collection route path (e.g., "/api/products").
    /// Used for constructing HATEOAS links. The batch suffix has already
    /// been stripped by the batch handler before assignment.
    /// </summary>
    internal required string CollectionPath { get; init; }
}
