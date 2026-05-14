namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents a product entry in an order.
/// </summary>
public class OrderItem
{
    /// <summary>
    /// Gets or sets the order item identifier.
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
    /// Gets or sets the product identifier.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the product.
    /// </summary>
    public Product? Product { get; set; }

    /// <summary>
    /// Gets or sets the product name captured for the order.
    /// </summary>
    public required string ProductName { get; set; }

    /// <summary>
    /// Gets or sets the quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the unit price captured for the order item.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Gets or sets the line total.
    /// </summary>
    public decimal LineTotal { get; set; }
}
