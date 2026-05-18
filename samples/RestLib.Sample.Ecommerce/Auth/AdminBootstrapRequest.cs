namespace RestLib.Sample.Ecommerce.Auth;

/// <summary>
/// Request body for creating the first administrator account.
/// </summary>
public sealed class AdminBootstrapRequest
{
    /// <summary>
    /// Gets or sets the administrator username.
    /// </summary>
    public required string UserName { get; set; }

    /// <summary>
    /// Gets or sets the administrator email address.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the administrator password.
    /// </summary>
    public required string Password { get; set; }
}
