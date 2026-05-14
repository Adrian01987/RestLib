using System.ComponentModel.DataAnnotations;

namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents carrier reference data managed by administrators.
/// </summary>
public class Carrier
{
    /// <summary>
    /// Gets or sets the carrier reference identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the associated carrier user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the carrier display name.
    /// </summary>
    [Required]
    [StringLength(120)]
    public required string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the carrier service area.
    /// </summary>
    [Required]
    [StringLength(120)]
    public required string ServiceArea { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the carrier can receive assignments.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
