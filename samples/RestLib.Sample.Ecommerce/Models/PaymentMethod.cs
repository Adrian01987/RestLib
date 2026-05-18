namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents payment method reference data exposed by the ecommerce sample.
/// </summary>
public class PaymentMethod
{
    /// <summary>
    /// Gets or sets the payment method key used by checkout.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Gets or sets the display name shown to clients.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the payment method description.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the payment method is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the display sort order.
    /// </summary>
    public int SortOrder { get; set; }
}
