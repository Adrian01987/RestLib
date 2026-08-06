using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Reuses immutable EF Core repository metadata and plan caches without extending
/// the lifetime of an EF Core model or repository options instance.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The resource-key type.</typeparam>
internal static class EfCoreRepositoryPlanCache<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    private static readonly ConditionalWeakTable<IModel, PerModelCache> ModelCaches = new();

    /// <summary>
    /// Gets the immutable planning bundle for the exact model, options instance,
    /// and current key-selector instance.
    /// </summary>
    /// <param name="model">The finalized EF Core model.</param>
    /// <param name="options">The repository options.</param>
    /// <returns>The reusable planning bundle.</returns>
    internal static EfCoreRepositoryPlanBundle<TEntity, TKey> GetOrCreate(
        IModel model,
        EfCoreRepositoryOptions<TEntity, TKey> options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        var modelCache = ModelCaches.GetValue(model, static _ => new PerModelCache());
        return modelCache.GetOrCreate(model, options, options.KeySelector);
    }

    private sealed class PerModelCache
    {
        private readonly ConditionalWeakTable<
            EfCoreRepositoryOptions<TEntity, TKey>,
            OptionsCacheEntry> _optionsCaches = new();

        internal EfCoreRepositoryPlanBundle<TEntity, TKey> GetOrCreate(
            IModel model,
            EfCoreRepositoryOptions<TEntity, TKey> options,
            Expression<Func<TEntity, TKey>>? keySelector)
        {
            var optionsCache = _optionsCaches.GetValue(
                options,
                static _ => new OptionsCacheEntry());
            return optionsCache.GetOrCreate(model, keySelector);
        }
    }

    private sealed class OptionsCacheEntry
    {
        private readonly object _gate = new();
        private EfCoreRepositoryPlanBundle<TEntity, TKey>? _bundle;
        private Expression<Func<TEntity, TKey>>? _keySelector;
        private bool _initialized;

        internal EfCoreRepositoryPlanBundle<TEntity, TKey> GetOrCreate(
            IModel model,
            Expression<Func<TEntity, TKey>>? keySelector)
        {
            lock (_gate)
            {
                if (!_initialized || !ReferenceEquals(_keySelector, keySelector))
                {
                    _keySelector = keySelector;
                    _bundle = new EfCoreRepositoryPlanBundle<TEntity, TKey>(model, keySelector);
                    _initialized = true;
                }

                return _bundle!;
            }
        }
    }
}

/// <summary>
/// Groups immutable key metadata with bounded query-plan caches for one repository
/// model/options/key-selector identity.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The resource-key type.</typeparam>
internal sealed class EfCoreRepositoryPlanBundle<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreRepositoryPlanBundle{TEntity, TKey}"/> class.
    /// </summary>
    /// <param name="model">The finalized EF Core model.</param>
    /// <param name="keySelector">The resource-key selector snapshot.</param>
    internal EfCoreRepositoryPlanBundle(
        IModel model,
        Expression<Func<TEntity, TKey>>? keySelector)
    {
        KeyMetadata = new EfCoreKeyMetadata<TEntity, TKey>(model, keySelector);
        PagePlanningCache = new EfCorePageQueryExecutor<TEntity>.PlanningCache();
        ProjectionPlanningCache = new EfCoreProjectionPlanner<TEntity>.PlanningCache();
    }

    /// <summary>
    /// Gets the resolved and compiled resource-key metadata.
    /// </summary>
    internal EfCoreKeyMetadata<TEntity, TKey> KeyMetadata { get; }

    /// <summary>
    /// Gets the bounded keyset-plan cache.
    /// </summary>
    internal EfCorePageQueryExecutor<TEntity>.PlanningCache PagePlanningCache { get; }

    /// <summary>
    /// Gets the bounded projection-plan cache.
    /// </summary>
    internal EfCoreProjectionPlanner<TEntity>.PlanningCache ProjectionPlanningCache { get; }
}
