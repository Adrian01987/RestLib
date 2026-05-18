using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Payments;

/// <summary>
/// Resolves payment strategies from keyed dependency injection registrations.
/// </summary>
public sealed class KeyedPaymentStrategyResolver : IPaymentStrategyResolver
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedPaymentStrategyResolver"/> class.
    /// </summary>
    /// <param name="services">The request service provider.</param>
    public KeyedPaymentStrategyResolver(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc />
    public IPaymentStrategy Resolve(Order order)
    {
        var method = NormalizeMethod(order.PaymentMethod);
        return TryResolve(order, out var strategy)
            ? strategy
            : throw new InvalidOperationException(
                $"No payment strategy is registered for method '{method}'.");
    }

    /// <inheritdoc />
    public bool TryResolve(Order order, [NotNullWhen(true)] out IPaymentStrategy? strategy)
    {
        ArgumentNullException.ThrowIfNull(order);

        var method = NormalizeMethod(order.PaymentMethod);
        if (string.IsNullOrWhiteSpace(method))
        {
            strategy = null;
            return false;
        }

        strategy = _services.GetKeyedService<IPaymentStrategy>(method);
        return strategy is not null;
    }

    private string NormalizeMethod(string? method)
    {
        return string.IsNullOrWhiteSpace(method)
            ? string.Empty
            : method.Trim().ToLowerInvariant();
    }
}
