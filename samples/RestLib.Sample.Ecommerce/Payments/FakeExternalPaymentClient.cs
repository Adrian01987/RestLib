using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RestLib.Sample.Ecommerce.Payments;

/// <summary>
/// Fake external payment processor used by the ecommerce sample.
/// </summary>
public sealed class FakeExternalPaymentClient
{
    private readonly ILogger<FakeExternalPaymentClient> _logger;
    private readonly IOptions<FakeExternalPaymentOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeExternalPaymentClient"/> class.
    /// </summary>
    /// <param name="options">The fake payment options.</param>
    /// <param name="logger">The logger.</param>
    public FakeExternalPaymentClient(
        IOptions<FakeExternalPaymentOptions> options,
        ILogger<FakeExternalPaymentClient> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Processes a fake external payment request.
    /// </summary>
    /// <param name="request">The fake payment request.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The fake payment result.</returns>
    public async Task<PaymentStrategyResult> ProcessAsync(FakeExternalPaymentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        var latencyMilliseconds = Math.Max(0, options.LatencyMilliseconds);
        if (latencyMilliseconds > 0)
        {
            await Task.Delay(latencyMilliseconds, ct);
        }

        var failureRate = Math.Clamp(options.FailureRate, 0, 1);
        if (failureRate > 0 && Random.Shared.NextDouble() < failureRate)
        {
            _logger.LogInformation(
                "Fake payment failure for order {OrderId} using {PaymentMethod}.",
                request.OrderId,
                request.PaymentMethod);

            return PaymentStrategyResult.Failure(
                options.FailureErrorCode,
                $"The fake {request.PaymentMethod} processor declined the payment.");
        }

        var externalReference =
            $"fake-{request.PaymentMethod}-{request.OrderId:N}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        _logger.LogInformation(
            "Fake payment success for order {OrderId} using {PaymentMethod}; reference {ExternalReference}.",
            request.OrderId,
            request.PaymentMethod,
            externalReference);

        return PaymentStrategyResult.Success(externalReference);
    }
}

/// <summary>
/// Request sent to the fake external payment client.
/// </summary>
public sealed class FakeExternalPaymentRequest
{
    /// <summary>
    /// Gets or sets the order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the payment method.
    /// </summary>
    public required string PaymentMethod { get; set; }

    /// <summary>
    /// Gets or sets the payment amount.
    /// </summary>
    public decimal Amount { get; set; }
}
