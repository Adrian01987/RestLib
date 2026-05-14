namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents a customer order.
/// </summary>
public class Order
{
    /// <summary>
    /// Gets or sets the order identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the owning customer identifier.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Gets or sets the owning customer.
    /// </summary>
    public User? Customer { get; set; }

    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public string Status { get; set; } = "PLACED";

    /// <summary>
    /// Gets or sets the selected payment method.
    /// </summary>
    public required string PaymentMethod { get; set; }

    /// <summary>
    /// Gets or sets the order total.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Gets the order items.
    /// </summary>
    public List<OrderItem> Items { get; } = [];

    /// <summary>
    /// Gets or sets the payment record.
    /// </summary>
    public Payment? Payment { get; set; }

    /// <summary>
    /// Gets or sets the shipment record.
    /// </summary>
    public Shipment? Shipment { get; set; }
}
