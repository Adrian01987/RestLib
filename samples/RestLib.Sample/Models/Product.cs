using System.ComponentModel.DataAnnotations;

namespace RestLib.Sample.Models;

/// <summary>
/// Represents a product in the catalog.
/// </summary>
public class Product
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(200)]
    public required string Name { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(0.01, (double)decimal.MaxValue)]
    public decimal Price { get; set; }

    public Guid CategoryId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
