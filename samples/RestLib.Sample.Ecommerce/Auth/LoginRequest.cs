namespace RestLib.Sample.Ecommerce.Auth;

/// <summary>
/// Request body for login.
/// </summary>
public sealed class LoginRequest
{
    /// <summary>
    /// Gets or sets the username or email address.
    /// </summary>
    public required string UserNameOrEmail { get; set; }

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public required string Password { get; set; }
}
