namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents a product category in the catalog.
/// </summary>
public class Category
{
    /// <summary>
    /// Gets or sets the category identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the category name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the URL-friendly category slug.
    /// </summary>
    public required string Slug { get; set; }

    /// <summary>
    /// Gets or sets the category description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the category is visible.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets the products in this category.
    /// </summary>
    public List<Product> Products { get; } = [];
}
