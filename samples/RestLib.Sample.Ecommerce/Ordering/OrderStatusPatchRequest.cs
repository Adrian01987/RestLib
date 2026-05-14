namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Request body for admin order status patches.
/// </summary>
public sealed class OrderStatusPatchRequest
{
    /// <summary>
    /// Gets or sets the target order status.
    /// </summary>
    public required string Status { get; set; }
}
