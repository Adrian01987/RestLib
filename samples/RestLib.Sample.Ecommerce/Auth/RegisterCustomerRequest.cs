namespace RestLib.Sample.Ecommerce.Auth;

/// <summary>
/// Request body for customer self-registration.
/// </summary>
public sealed class RegisterCustomerRequest
{
    /// <summary>
    /// Gets or sets the customer username.
    /// </summary>
    public required string UserName { get; set; }

    /// <summary>
    /// Gets or sets the customer's email address.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the customer password.
    /// </summary>
    public required string Password { get; set; }
}
