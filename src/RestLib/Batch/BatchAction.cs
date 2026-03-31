namespace RestLib.Batch;

/// <summary>
/// Defines the available batch operation types.
/// </summary>
public enum BatchAction
{
    /// <summary>Create multiple entities.</summary>
    Create,

    /// <summary>Fully replace multiple entities (PUT semantics).</summary>
    Update,

    /// <summary>Partially update multiple entities (PATCH semantics).</summary>
    Patch,

    /// <summary>Delete multiple entities.</summary>
    Delete
}
