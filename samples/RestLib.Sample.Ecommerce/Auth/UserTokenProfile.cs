namespace RestLib.Sample.Ecommerce.Auth;

/// <summary>
/// User details included with an issued token.
/// </summary>
public sealed class UserTokenProfile
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public required string UserName { get; set; }

    /// <summary>
    /// Gets or sets the email address.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the role name.
    /// </summary>
    public required string Role { get; set; }
}
