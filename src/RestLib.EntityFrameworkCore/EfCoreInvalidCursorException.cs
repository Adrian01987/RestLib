namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Represents an invalid EF Core adapter pagination cursor.
/// </summary>
public sealed class EfCoreInvalidCursorException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreInvalidCursorException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public EfCoreInvalidCursorException(string message)
        : base(message)
    {
    }
}
