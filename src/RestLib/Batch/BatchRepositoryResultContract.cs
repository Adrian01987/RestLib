namespace RestLib.Batch;

/// <summary>
/// Validates and correlates results returned by <see cref="Abstractions.IBatchRepository{TEntity, TKey}"/>.
/// </summary>
internal static class BatchRepositoryResultContract
{
    /// <summary>
    /// Captures caller-supplied create keys whose ordering can be checked after
    /// persistence. Default keys are excluded because a repository may generate them.
    /// </summary>
    /// <typeparam name="TEntity">The submitted entity type.</typeparam>
    /// <typeparam name="TKey">The resource key type.</typeparam>
    /// <param name="entities">The entities in submission order.</param>
    /// <param name="keySelector">Extracts a resource key from an entity.</param>
    /// <returns>Observable keys indexed by their submission position.</returns>
    internal static IReadOnlyDictionary<int, TKey> CaptureObservableCreateKeys<TEntity, TKey>(
        IReadOnlyList<TEntity> entities,
        Func<TEntity, TKey?> keySelector)
        where TEntity : class
        where TKey : notnull
    {
        var keys = new Dictionary<int, TKey>();
        for (var index = 0; index < entities.Count; index++)
        {
            var key = keySelector(entities[index]);
            if (key is not null && !EqualityComparer<TKey>.Default.Equals(key, default!))
            {
                keys.Add(index, key);
            }
        }

        return keys;
    }

    /// <summary>
    /// Validates a bulk result that must contain exactly one entity per input item.
    /// </summary>
    /// <typeparam name="TEntity">The returned entity type.</typeparam>
    /// <param name="results">The entities returned by the repository.</param>
    /// <param name="expectedCount">The number of submitted entities.</param>
    /// <param name="action">The batch action name.</param>
    internal static void ValidateComplete<TEntity>(
        IReadOnlyList<TEntity> results,
        int expectedCount,
        string action)
        where TEntity : class
    {
        ValidateNonNull(results, action);

        if (results.Count != expectedCount)
        {
            throw Violation(
                action,
                $"returned {results.Count} entities for {expectedCount} submitted entities");
        }
    }

    /// <summary>
    /// Validates that a bulk result contains no null entities.
    /// </summary>
    /// <typeparam name="TEntity">The returned entity type.</typeparam>
    /// <param name="results">The entities returned by the repository.</param>
    /// <param name="action">The batch action name.</param>
    internal static void ValidateNonNull<TEntity>(IReadOnlyList<TEntity> results, string action)
        where TEntity : class
    {
        if (results is null)
        {
            throw Violation(action, "returned a null result list");
        }

        if (results.Any(static result => result is null))
        {
            throw Violation(action, "returned a null entity");
        }
    }

    /// <summary>
    /// Correlates update or patch results to their submitted keys. A key may be
    /// omitted completely when the resource no longer exists, but partial duplicate
    /// groups, unexpected keys, and reordered results violate the repository contract.
    /// </summary>
    /// <typeparam name="TKey">The resource key type.</typeparam>
    /// <param name="submittedKeys">The keys in submission order.</param>
    /// <param name="returnedKeys">The keys in repository-result order.</param>
    /// <param name="action">The batch action name.</param>
    /// <returns>
    /// One result index per submitted key, with <c>null</c> for keys omitted by the repository.
    /// </returns>
    internal static IReadOnlyList<int?> CorrelateByKey<TKey>(
        IReadOnlyList<TKey> submittedKeys,
        IReadOnlyList<TKey> returnedKeys,
        string action)
        where TKey : notnull
    {
        if (returnedKeys.Count > submittedKeys.Count)
        {
            throw Violation(
                action,
                $"returned {returnedKeys.Count} entities for {submittedKeys.Count} submitted entities");
        }

        var submittedCounts = CountKeys(submittedKeys);
        var returnedCounts = CountKeys(returnedKeys);

        foreach (var (key, returnedCount) in returnedCounts)
        {
            if (!submittedCounts.TryGetValue(key, out var submittedCount))
            {
                throw Violation(action, "returned an entity whose key was not submitted");
            }

            if (returnedCount != submittedCount)
            {
                throw Violation(
                    action,
                    "returned only part of a repeated-key result group");
            }
        }

        var expectedReturnedKeys = submittedKeys
            .Where(returnedCounts.ContainsKey)
            .ToList();

        if (!expectedReturnedKeys.SequenceEqual(returnedKeys))
        {
            throw Violation(action, "returned entities in a different order from the submitted items");
        }

        var returnedIndexes = new Dictionary<TKey, Queue<int>>();
        for (var index = 0; index < returnedKeys.Count; index++)
        {
            var key = returnedKeys[index];
            if (!returnedIndexes.TryGetValue(key, out var indexes))
            {
                indexes = new Queue<int>();
                returnedIndexes.Add(key, indexes);
            }

            indexes.Enqueue(index);
        }

        var correlations = new int?[submittedKeys.Count];
        for (var index = 0; index < submittedKeys.Count; index++)
        {
            if (returnedIndexes.TryGetValue(submittedKeys[index], out var indexes))
            {
                correlations[index] = indexes.Dequeue();
            }
        }

        return correlations;
    }

    /// <summary>
    /// Validates the count returned by a bulk delete operation.
    /// </summary>
    /// <param name="expectedCount">The expected number of distinct deletions.</param>
    /// <param name="actualCount">The count returned by the repository.</param>
    internal static void ValidateDeletedCount(int expectedCount, int actualCount)
    {
        if (actualCount != expectedCount)
        {
            throw Violation(
                "delete",
                $"reported {actualCount} deletions for {expectedCount} distinct submitted keys");
        }
    }

    private static Dictionary<TKey, int> CountKeys<TKey>(IReadOnlyList<TKey> keys)
        where TKey : notnull
    {
        var counts = new Dictionary<TKey, int>();
        foreach (var key in keys)
        {
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }

    private static BatchRepositoryContractException Violation(string action, string detail)
    {
        return new BatchRepositoryContractException(
            $"The batch repository contract was violated for '{action}': it {detail}.");
    }
}

/// <summary>
/// Identifies a malformed or uncorrelatable result returned by a batch repository.
/// </summary>
internal sealed class BatchRepositoryContractException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BatchRepositoryContractException"/> class.
    /// </summary>
    /// <param name="message">The contract violation description.</param>
    internal BatchRepositoryContractException(string message)
        : base(message)
    {
    }
}
