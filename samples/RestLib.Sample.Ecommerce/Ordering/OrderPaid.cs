namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Domain event raised after an order payment succeeds and commits.
/// </summary>
public sealed class OrderPaid : IDomainEvent
{
    /// <summary>
    /// Gets or sets the order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the payment identifier.
    /// </summary>
    public Guid PaymentId { get; set; }

    /// <summary>
    /// Gets or sets the customer identifier.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Gets or sets the payment method.
    /// </summary>
    public required string PaymentMethod { get; set; }

    /// <summary>
    /// Gets or sets the external payment reference.
    /// </summary>
    public required string ExternalReference { get; set; }

    /// <summary>
    /// Gets or sets the order status after payment.
    /// </summary>
    public required string OrderStatus { get; set; }

    /// <summary>
    /// Gets or sets the order total.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    /// Gets or sets the event timestamp.
    /// </summary>
    public DateTime OccurredAt { get; set; }
}
