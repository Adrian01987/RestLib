namespace RestLib.Batch;

/// <summary>
/// Marks the boundary around a bulk repository operation so its failures can be
/// distinguished from failures in post-persistence processing.
/// </summary>
internal static class BulkPersistenceExecutor
{
    /// <summary>
    /// Executes a bulk repository operation and wraps any failure for the batch pipeline.
    /// </summary>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The bulk repository operation.</param>
    /// <returns>The operation result.</returns>
    internal static async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception)
        {
            throw new BulkPersistenceException(exception);
        }
    }
}

/// <summary>
/// Identifies an exception raised by a bulk repository operation.
/// </summary>
internal sealed class BulkPersistenceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BulkPersistenceException"/> class.
    /// </summary>
    /// <param name="innerException">The original repository exception.</param>
    internal BulkPersistenceException(Exception innerException)
        : base("A bulk repository operation failed.", innerException)
    {
    }
}
