namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents a customer-owned delivery address.
/// </summary>
public class Address
{
    /// <summary>
    /// Gets or sets the address identifier.
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
    /// Gets or sets the first street address line.
    /// </summary>
    public required string Line1 { get; set; }

    /// <summary>
    /// Gets or sets the optional second street address line.
    /// </summary>
    public string? Line2 { get; set; }

    /// <summary>
    /// Gets or sets the city.
    /// </summary>
    public required string City { get; set; }

    /// <summary>
    /// Gets or sets the state, province, or region.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Gets or sets the postal code.
    /// </summary>
    public required string PostalCode { get; set; }

    /// <summary>
    /// Gets or sets the two-letter country code.
    /// </summary>
    public required string CountryCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the primary address.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
