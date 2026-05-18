namespace RestLib.Sample.Ecommerce.Storefront;

/// <summary>
/// Request body for customer profile updates.
/// </summary>
public sealed class UpdateProfileRequest
{
    /// <summary>
    /// Gets or sets the updated username.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the updated email address.
    /// </summary>
    public string? Email { get; set; }
}
