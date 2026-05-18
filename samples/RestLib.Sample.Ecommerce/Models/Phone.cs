namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents a customer-owned phone number.
/// </summary>
public class Phone
{
    /// <summary>
    /// Gets or sets the phone identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the owning customer identifier.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Gets or sets the owning customer.
    /// </summary>
    public User? Customer { get; set; }

    /// <summary>
    /// Gets or sets the phone number.
    /// </summary>
    public required string Number { get; set; }

    /// <summary>
    /// Gets or sets the phone number type.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the primary phone number.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
