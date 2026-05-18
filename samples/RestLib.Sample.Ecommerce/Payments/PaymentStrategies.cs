using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Payments;

/// <summary>
/// Card payment strategy.
/// </summary>
public sealed class CardPaymentStrategy : IPaymentStrategy
{
    private readonly FakeExternalPaymentClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="CardPaymentStrategy"/> class.
    /// </summary>
    /// <param name="client">The fake external payment client.</param>
    public CardPaymentStrategy(FakeExternalPaymentClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public string Method => PaymentMethods.Card;

    /// <inheritdoc />
    public Task<PaymentStrategyResult> ProcessAsync(Order order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);

        var request = new FakeExternalPaymentRequest
        {
            OrderId = order.Id,
            PaymentMethod = Method,
            Amount = order.Total,
        };

        return _client.ProcessAsync(request, ct);
    }
}

/// <summary>
/// PayPal payment strategy.
/// </summary>
public sealed class PayPalPaymentStrategy : IPaymentStrategy
{
    private readonly FakeExternalPaymentClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PayPalPaymentStrategy"/> class.
    /// </summary>
    /// <param name="client">The fake external payment client.</param>
    public PayPalPaymentStrategy(FakeExternalPaymentClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public string Method => PaymentMethods.PayPal;

    /// <inheritdoc />
    public Task<PaymentStrategyResult> ProcessAsync(Order order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);

        var request = new FakeExternalPaymentRequest
        {
            OrderId = order.Id,
            PaymentMethod = Method,
            Amount = order.Total,
        };

        return _client.ProcessAsync(request, ct);
    }
}

/// <summary>
/// Bank transfer payment strategy.
/// </summary>
public sealed class BankTransferPaymentStrategy : IPaymentStrategy
{
    private readonly FakeExternalPaymentClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="BankTransferPaymentStrategy"/> class.
    /// </summary>
    /// <param name="client">The fake external payment client.</param>
    public BankTransferPaymentStrategy(FakeExternalPaymentClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public string Method => PaymentMethods.BankTransfer;

    /// <inheritdoc />
    public Task<PaymentStrategyResult> ProcessAsync(Order order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);

        var request = new FakeExternalPaymentRequest
        {
            OrderId = order.Id,
            PaymentMethod = Method,
            Amount = order.Total,
        };

        return _client.ProcessAsync(request, ct);
    }
}
