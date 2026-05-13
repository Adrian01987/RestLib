namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents a customer's shopping cart.
/// </summary>
public class Cart
{
    /// <summary>
    /// Gets or sets the cart identifier.
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
    /// Gets or sets the cart status.
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets the cart items.
    /// </summary>
    public List<CartItem> Items { get; } = [];
}
