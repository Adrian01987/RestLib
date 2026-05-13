namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents the fulfillment record for an order.
/// </summary>
public class Shipment
{
    /// <summary>
    /// Gets or sets the shipment identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the order.
    /// </summary>
    public Order? Order { get; set; }

    /// <summary>
    /// Gets or sets the assigned carrier identifier.
    /// </summary>
    public Guid? CarrierId { get; set; }

    /// <summary>
    /// Gets or sets the assigned carrier.
    /// </summary>
    public User? Carrier { get; set; }

    /// <summary>
    /// Gets or sets the shipment status.
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets the shipment events.
    /// </summary>
    public List<ShipmentEvent> Events { get; } = [];
}
