namespace RestLib.Internal;

/// <summary>
/// Identifies cancellation exceptions that belong to the current request.
/// </summary>
internal static class RequestCancellation
{
    /// <summary>
    /// Determines whether an exception represents cancellation of the supplied request token.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <param name="requestToken">The current request cancellation token.</param>
    /// <returns>
    /// <c>true</c> when the exception is an <see cref="OperationCanceledException"/>
    /// and the request token has been cancelled; otherwise, <c>false</c>.
    /// </returns>
    internal static bool IsRequested(Exception exception, CancellationToken requestToken)
    {
        return exception is OperationCanceledException && requestToken.IsCancellationRequested;
    }
}
