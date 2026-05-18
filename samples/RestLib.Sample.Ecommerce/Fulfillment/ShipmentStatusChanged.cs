using RestLib.Sample.Ecommerce.Ordering;

namespace RestLib.Sample.Ecommerce.Fulfillment;

/// <summary>
/// Domain event raised after a shipment status event updates fulfillment state.
/// </summary>
public sealed class ShipmentStatusChanged : IDomainEvent
{
    /// <summary>
    /// Gets or sets the order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the shipment identifier.
    /// </summary>
    public Guid ShipmentId { get; set; }

    /// <summary>
    /// Gets or sets the customer identifier.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Gets or sets the carrier user identifier.
    /// </summary>
    public Guid CarrierUserId { get; set; }

    /// <summary>
    /// Gets or sets the carrier display name.
    /// </summary>
    public required string CarrierDisplayName { get; set; }

    /// <summary>
    /// Gets or sets the order status after propagation.
    /// </summary>
    public required string OrderStatus { get; set; }

    /// <summary>
    /// Gets or sets the shipment status after propagation.
    /// </summary>
    public required string ShipmentStatus { get; set; }

    /// <summary>
    /// Gets or sets the optional event notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the event timestamp.
    /// </summary>
    public DateTime OccurredAt { get; set; }
}
