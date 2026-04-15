using System.ComponentModel.DataAnnotations;

namespace RestLib.Sample.Models;

/// <summary>
/// A customer entity backed by EF Core with SQLite.
/// </summary>
public class Customer
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(100)]
    public required string Name { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(200)]
    public required string Email { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
