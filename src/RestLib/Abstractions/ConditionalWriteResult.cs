namespace RestLib.Abstractions;

/// <summary>
/// Describes the outcome of an atomic conditional repository mutation.
/// </summary>
public enum ConditionalWriteStatus
{
    /// <summary>
    /// The precondition succeeded and the mutation was persisted.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The target entity did not exist when the atomic operation ran.
    /// </summary>
    NotFound,

    /// <summary>
    /// The entity existed, but its current state did not satisfy the precondition.
    /// </summary>
    PreconditionFailed
}

/// <summary>
/// Represents the outcome and entity value from an atomic conditional repository mutation.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class ConditionalWriteResult<TEntity>
    where TEntity : class
{
    private ConditionalWriteResult(ConditionalWriteStatus status, TEntity? entity)
    {
        Status = status;
        Entity = entity;
    }

    /// <summary>
    /// Gets the mutation outcome.
    /// </summary>
    public ConditionalWriteStatus Status { get; }

    /// <summary>
    /// Gets the persisted or deleted entity when <see cref="Status"/> is
    /// <see cref="ConditionalWriteStatus.Succeeded"/>; otherwise, <see langword="null"/>.
    /// </summary>
    public TEntity? Entity { get; }

    /// <summary>
    /// Creates a successful conditional-write result.
    /// </summary>
    /// <param name="entity">The persisted or deleted entity.</param>
    /// <returns>A successful result containing <paramref name="entity"/>.</returns>
    public static ConditionalWriteResult<TEntity> Success(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new ConditionalWriteResult<TEntity>(ConditionalWriteStatus.Succeeded, entity);
    }

    /// <summary>
    /// Creates a result indicating that the target entity was not found.
    /// </summary>
    /// <returns>A not-found result.</returns>
    public static ConditionalWriteResult<TEntity> NotFound()
    {
        return new ConditionalWriteResult<TEntity>(ConditionalWriteStatus.NotFound, null);
    }

    /// <summary>
    /// Creates a result indicating that the current entity did not satisfy the precondition.
    /// </summary>
    /// <returns>A precondition-failed result.</returns>
    public static ConditionalWriteResult<TEntity> PreconditionFailed()
    {
        return new ConditionalWriteResult<TEntity>(ConditionalWriteStatus.PreconditionFailed, null);
    }
}
