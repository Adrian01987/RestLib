namespace RestLib.Sample.Ecommerce.Payments;

/// <summary>
/// Supported ecommerce sample payment method keys.
/// </summary>
public static class PaymentMethods
{
    /// <summary>
    /// Gets the card payment method key.
    /// </summary>
    public const string Card = "card";

    /// <summary>
    /// Gets the PayPal payment method key.
    /// </summary>
    public const string PayPal = "paypal";

    /// <summary>
    /// Gets the bank transfer payment method key.
    /// </summary>
    public const string BankTransfer = "bank_transfer";
}
