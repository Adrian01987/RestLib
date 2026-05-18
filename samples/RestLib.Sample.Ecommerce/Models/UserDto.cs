using System.ComponentModel.DataAnnotations;

namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// API model for administering users without exposing persistence-only secrets.
/// </summary>
public class UserDto
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the login name.
    /// </summary>
    [Required]
    [StringLength(100)]
    public required string UserName { get; set; }

    /// <summary>
    /// Gets or sets the user's email address.
    /// </summary>
    [Required]
    [EmailAddress]
    [StringLength(200)]
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the role name.
    /// </summary>
    [Required]
    [StringLength(40)]
    public required string Role { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user can sign in.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
