namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Request body for storefront checkout.
/// </summary>
public sealed class CheckoutRequest
{
    /// <summary>
    /// Gets or sets the payment method to capture on the order.
    /// </summary>
    public string PaymentMethod { get; set; } = "card";
}
