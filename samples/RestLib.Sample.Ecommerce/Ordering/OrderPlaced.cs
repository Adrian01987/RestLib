namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Domain event raised after an order is placed and committed.
/// </summary>
public sealed class OrderPlaced : IDomainEvent
{
    /// <summary>
    /// Gets or sets the order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the customer identifier.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Gets or sets the shipment identifier created for the order.
    /// </summary>
    public Guid ShipmentId { get; set; }

    /// <summary>
    /// Gets or sets the order total.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    /// Gets or sets the event timestamp.
    /// </summary>
    public DateTime OccurredAt { get; set; }
}
