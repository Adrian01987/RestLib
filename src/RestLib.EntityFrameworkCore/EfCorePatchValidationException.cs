using RestLib.Abstractions;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Represents a strict PATCH validation failure for unknown or forbidden fields.
/// </summary>
public sealed class EfCorePatchValidationException : PatchValidationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EfCorePatchValidationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public EfCorePatchValidationException(string message)
        : base(message)
    {
    }
}
