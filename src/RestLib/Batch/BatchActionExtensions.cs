namespace RestLib.Batch;

/// <summary>
/// Maps batch request actions to their corresponding RestLib operations.
/// </summary>
internal static class BatchActionExtensions
{
    /// <summary>
    /// Gets the RestLib operation represented by a batch action.
    /// </summary>
    /// <param name="action">The batch action.</param>
    /// <returns>The corresponding RestLib operation.</returns>
    internal static RestLibOperation ToRestLibOperation(this BatchAction action) => action switch
    {
        BatchAction.Create => RestLibOperation.BatchCreate,
        BatchAction.Update => RestLibOperation.BatchUpdate,
        BatchAction.Patch => RestLibOperation.BatchPatch,
        BatchAction.Delete => RestLibOperation.BatchDelete,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown batch action.")
    };
}
