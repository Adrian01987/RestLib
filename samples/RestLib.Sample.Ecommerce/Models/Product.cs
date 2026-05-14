namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents a product in the ecommerce catalog.
/// </summary>
public class Product
{
    /// <summary>
    /// Gets or sets the product identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the category identifier.
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public Category? Category { get; set; }

    /// <summary>
    /// Gets or sets the stock keeping unit.
    /// </summary>
    public required string Sku { get; set; }

    /// <summary>
    /// Gets or sets the product name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the product description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the product price.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Gets or sets the available stock count.
    /// </summary>
    public int StockOnHand { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the product is visible.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the concurrency token used for ETags.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}
