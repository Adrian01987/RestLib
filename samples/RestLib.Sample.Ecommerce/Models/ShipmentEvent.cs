namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents an append-only shipment status event.
/// </summary>
public class ShipmentEvent
{
    /// <summary>
    /// Gets or sets the shipment event identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the shipment identifier.
    /// </summary>
    public Guid ShipmentId { get; set; }

    /// <summary>
    /// Gets or sets the shipment.
    /// </summary>
    public Shipment? Shipment { get; set; }

    /// <summary>
    /// Gets or sets the event status.
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Gets or sets event notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the event timestamp.
    /// </summary>
    public DateTime OccurredAt { get; set; }
}
