namespace RestLib.Pagination;

/// <summary>
/// Represents a pagination cursor that cannot be consumed by a repository.
/// </summary>
public class InvalidCursorException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidCursorException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidCursorException(string message)
        : base(message)
    {
    }
}
