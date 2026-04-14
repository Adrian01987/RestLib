namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Represents a database constraint violation detected by the EF Core adapter.
/// </summary>
public class EfCoreConstraintViolationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreConstraintViolationException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="constraintType">The classified constraint type.</param>
    /// <param name="innerException">The original exception.</param>
    public EfCoreConstraintViolationException(
        string message,
        EfCoreConstraintType constraintType,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ConstraintType = constraintType;
    }

    /// <summary>
    /// Gets the classified database constraint type.
    /// </summary>
    public EfCoreConstraintType ConstraintType { get; }
}
