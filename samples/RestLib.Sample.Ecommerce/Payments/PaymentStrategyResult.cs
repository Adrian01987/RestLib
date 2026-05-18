namespace RestLib.Sample.Ecommerce.Payments;

/// <summary>
/// Result returned by a payment strategy.
/// </summary>
public sealed class PaymentStrategyResult
{
    /// <summary>
    /// Gets a value indicating whether payment succeeded.
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// Gets the external payment reference when payment succeeds.
    /// </summary>
    public string? ExternalReference { get; init; }

    /// <summary>
    /// Gets the machine-readable error code when payment fails.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Gets the human-readable error message when payment fails.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful payment result.
    /// </summary>
    /// <param name="externalReference">The external payment reference.</param>
    /// <returns>The successful result.</returns>
    public static PaymentStrategyResult Success(string externalReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalReference);

        return new PaymentStrategyResult
        {
            Succeeded = true,
            ExternalReference = externalReference,
        };
    }

    /// <summary>
    /// Creates a failed payment result.
    /// </summary>
    /// <param name="errorCode">The machine-readable error code.</param>
    /// <param name="errorMessage">The human-readable error message.</param>
    /// <returns>The failed result.</returns>
    public static PaymentStrategyResult Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new PaymentStrategyResult
        {
            Succeeded = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
        };
    }
}
