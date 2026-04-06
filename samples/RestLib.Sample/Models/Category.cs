using System.ComponentModel.DataAnnotations;

namespace RestLib.Sample.Models;

/// <summary>
/// Represents a product category.
/// </summary>
public class Category
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(100)]
    public required string Name { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}
