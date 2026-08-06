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
internal sealed class BatchContext<TEntity, TKey> : BatchPipelineContext<TKey>
    where TEntity : class
    where TKey : notnull
{
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
    /// Gets the endpoint configuration. Used for HATEOAS link generation
    /// to determine which operations are enabled.
    /// </summary>
    internal required RestLibEndpointConfiguration<TEntity, TKey> EndpointConfig { get; init; }
}
