namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents country reference data exposed by the ecommerce sample.
/// </summary>
public class Country
{
    /// <summary>
    /// Gets or sets the ISO 3166-1 alpha-2 country code.
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Gets or sets the country display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the broad commerce region.
    /// </summary>
    public required string Region { get; set; }

    /// <summary>
    /// Gets or sets the default currency code.
    /// </summary>
    public required string CurrencyCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether storefront shipping is enabled.
    /// </summary>
    public bool IsShippingEnabled { get; set; } = true;
}
