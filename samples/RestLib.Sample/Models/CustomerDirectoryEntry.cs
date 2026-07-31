using System.ComponentModel.DataAnnotations;

namespace RestLib.Sample.Models;

/// <summary>
/// Public customer-directory representation whose email address is the resource key.
/// The mapped EF entity retains a separate internal <see cref="Customer.Id"/> primary key.
/// </summary>
public sealed class CustomerDirectoryEntry
{
    /// <summary>
    /// Gets or sets the public resource key.
    /// </summary>
    [Required]
    [EmailAddress]
    [StringLength(200)]
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    [Required]
    [StringLength(100)]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the city.
    /// </summary>
    [StringLength(100)]
    public string? City { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the directory entry is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
