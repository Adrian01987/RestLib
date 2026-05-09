using System.ComponentModel.DataAnnotations;

namespace RestLib.Sample.Models;

/// <summary>
/// API model used to demonstrate fluent two-model mapping for customer profiles.
/// </summary>
public class CustomerDto
{
    /// <summary>
    /// Gets or sets the customer identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    [Required]
    [StringLength(100)]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the email address.
    /// </summary>
    [Required]
    [EmailAddress]
    [StringLength(200)]
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the city.
    /// </summary>
    [StringLength(100)]
    public string? City { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the customer profile is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets when the customer was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
