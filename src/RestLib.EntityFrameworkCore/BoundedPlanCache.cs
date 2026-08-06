namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Stores a bounded number of immutable planning results.
/// </summary>
/// <typeparam name="TKey">The normalized plan-shape key type.</typeparam>
/// <typeparam name="TValue">The immutable planning result type.</typeparam>
internal sealed class BoundedPlanCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _entries = [];
    private readonly object _gate = new();
    private readonly int _capacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="BoundedPlanCache{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="capacity">The maximum number of shapes retained by the cache.</param>
    internal BoundedPlanCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>
    /// Gets the number of retained planning results.
    /// </summary>
    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// Gets an existing result or creates a result for the supplied shape.
    /// Results created after the cache reaches its capacity are returned without
    /// being retained.
    /// </summary>
    /// <param name="key">The normalized plan shape.</param>
    /// <param name="valueFactory">Creates the immutable planning result.</param>
    /// <returns>The cached or newly-created result.</returns>
    internal TValue GetOrCreate(TKey key, Func<TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cachedValue))
            {
                return cachedValue;
            }

            var value = valueFactory();
            if (_entries.Count < _capacity)
            {
                _entries.Add(key, value);
            }

            return value;
        }
    }
}
