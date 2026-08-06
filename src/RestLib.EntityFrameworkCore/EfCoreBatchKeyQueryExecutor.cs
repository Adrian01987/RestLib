using Microsoft.EntityFrameworkCore;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Executes bounded EF Core queries for sets of resource keys.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The resource-key type.</typeparam>
internal sealed class EfCoreBatchKeyQueryExecutor<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Bounds the key parameters represented by one database query.
    /// </summary>
    internal const int ParameterBudget = 512;

    private readonly EfCoreKeyMetadata<TEntity, TKey> _keyMetadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreBatchKeyQueryExecutor{TEntity, TKey}"/> class.
    /// </summary>
    /// <param name="keyMetadata">The resource-key metadata.</param>
    internal EfCoreBatchKeyQueryExecutor(EfCoreKeyMetadata<TEntity, TKey> keyMetadata)
    {
        _keyMetadata = keyMetadata ?? throw new ArgumentNullException(nameof(keyMetadata));
    }

    /// <summary>
    /// Fetches entities matching the supplied keys through sequential bounded queries.
    /// </summary>
    /// <param name="query">The caller-configured EF Core query.</param>
    /// <param name="keys">The resource keys to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entities returned by all key chunks.</returns>
    internal async Task<IReadOnlyList<TEntity>> FetchAsync(
        IQueryable<TEntity> query,
        IReadOnlyList<TKey> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return [];
        }

        var uniqueKeys = Deduplicate(keys);
        var chunkSize = ParameterBudget / _keyMetadata.KeyPartCount;
        var entities = new List<TEntity>();

        foreach (var keyChunk in uniqueKeys.Chunk(chunkSize))
        {
            ct.ThrowIfCancellationRequested();
            var predicate = _keyMetadata.BuildContainsPredicate(keyChunk);
            entities.AddRange(await query.Where(predicate).ToListAsync(ct));
        }

        return entities;
    }

    private static IReadOnlyList<TKey> Deduplicate(IReadOnlyList<TKey> keys)
    {
        var seen = new HashSet<TKey>();
        var uniqueKeys = new List<TKey>(keys.Count);

        foreach (var key in keys)
        {
            if (seen.Add(key))
            {
                uniqueKeys.Add(key);
            }
        }

        return uniqueKeys;
    }
}
