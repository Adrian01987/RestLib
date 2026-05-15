using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Payments;

/// <summary>
/// Processes payment for an order using a specific payment method.
/// </summary>
public interface IPaymentStrategy
{
    /// <summary>
    /// Gets the payment method key handled by the strategy.
    /// </summary>
    string Method { get; }

    /// <summary>
    /// Processes payment for the supplied order.
    /// </summary>
    /// <param name="order">The order being paid.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The payment processing result.</returns>
    Task<PaymentStrategyResult> ProcessAsync(Order order, CancellationToken ct);
}
