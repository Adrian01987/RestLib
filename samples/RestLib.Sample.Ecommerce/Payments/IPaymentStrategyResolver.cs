using System.Diagnostics.CodeAnalysis;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Payments;

/// <summary>
/// Resolves payment strategies for orders.
/// </summary>
public interface IPaymentStrategyResolver
{
    /// <summary>
    /// Resolves the strategy matching the order payment method.
    /// </summary>
    /// <param name="order">The order whose payment method should be resolved.</param>
    /// <returns>The matching payment strategy.</returns>
    IPaymentStrategy Resolve(Order order);

    /// <summary>
    /// Attempts to resolve the strategy matching the order payment method.
    /// </summary>
    /// <param name="order">The order whose payment method should be resolved.</param>
    /// <param name="strategy">The resolved strategy, when one is registered.</param>
    /// <returns><see langword="true"/> when a strategy was found.</returns>
    bool TryResolve(Order order, [NotNullWhen(true)] out IPaymentStrategy? strategy);
}
