namespace RestLib.EntityFrameworkCore;

internal sealed class EfCorePatchValidationException : Exception
{
    internal EfCorePatchValidationException(string message)
        : base(message)
    {
    }
}
