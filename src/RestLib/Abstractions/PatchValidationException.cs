namespace RestLib.Abstractions;

/// <summary>
/// Represents a client-correctable PATCH validation failure reported by a repository adapter.
/// </summary>
/// <remarks>
/// RestLib can return the exception message in a 400 response for direct or individually
/// processed PATCH operations. The message must therefore be safe to disclose to API clients
/// and must not contain internal persistence or infrastructure details.
/// </remarks>
public class PatchValidationException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchValidationException"/> class.
    /// </summary>
    /// <param name="message">The client-safe validation message.</param>
    public PatchValidationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchValidationException"/> class.
    /// </summary>
    /// <param name="message">The client-safe validation message.</param>
    /// <param name="innerException">The exception that caused the validation failure.</param>
    public PatchValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
