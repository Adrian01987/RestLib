using System.ComponentModel.DataAnnotations;

namespace RestLib.Sample.Models;

/// <summary>
/// Represents a product identified by an ordered tenant and SKU composite key.
/// </summary>
public sealed class TenantProduct
{
    /// <summary>
    /// Gets or sets the tenant key segment.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the SKU key segment.
    /// </summary>
    [Required]
    [StringLength(64)]
    public required string Sku { get; set; }

    /// <summary>
    /// Gets or sets the product name.
    /// </summary>
    [Required]
    [StringLength(200)]
    public required string ProductName { get; set; }

    /// <summary>
    /// Gets or sets the unit price.
    /// </summary>
    [Range(0.01, (double)decimal.MaxValue)]
    public decimal Price { get; set; }
}
