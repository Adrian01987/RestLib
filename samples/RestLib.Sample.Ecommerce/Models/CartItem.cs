using System.ComponentModel.DataAnnotations.Schema;
using RestLib;

namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents a product entry in a customer's cart.
/// </summary>
public class CartItem
{
    /// <summary>
    /// Gets or sets the RestLib composite key for the cart item.
    /// </summary>
    [NotMapped]
    public RestLibCompositeKey<Guid, Guid> Id
    {
        get => new(CartId, ProductId);
        set
        {
            CartId = value.First;
            ProductId = value.Second;
        }
    }

    /// <summary>
    /// Gets or sets the cart identifier.
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Gets or sets the cart.
    /// </summary>
    public Cart? Cart { get; set; }

    /// <summary>
    /// Gets or sets the product identifier.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the product.
    /// </summary>
    public Product? Product { get; set; }

    /// <summary>
    /// Gets or sets the quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the unit price captured for the cart item.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Gets or sets the line total.
    /// </summary>
    public decimal LineTotal { get; set; }
}
