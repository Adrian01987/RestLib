namespace RestLib.Sample.Ecommerce;

/// <summary>
/// Rate limiting policy names used by the ecommerce sample.
/// </summary>
public static class EcommerceRateLimitPolicies
{
    /// <summary>
    /// Policy for read-heavy storefront, carrier, and admin browse endpoints.
    /// </summary>
    public const string StorefrontRead = "storefront-read";

    /// <summary>
    /// Policy for customer, carrier, and support mutation endpoints.
    /// </summary>
    public const string StorefrontWrite = "storefront-write";

    /// <summary>
    /// Policy for checkout and payment commands that should be guarded more tightly.
    /// </summary>
    public const string CheckoutStrict = "checkout-strict";

    /// <summary>
    /// Policy for admin mutation and batch endpoints.
    /// </summary>
    public const string AdminBatch = "admin-batch";

    /// <summary>
    /// Policy for authentication and registration endpoints.
    /// </summary>
    public const string Auth = "auth";
}
