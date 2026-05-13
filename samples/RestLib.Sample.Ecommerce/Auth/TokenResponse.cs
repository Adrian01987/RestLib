namespace RestLib.Sample.Ecommerce.Auth;

/// <summary>
/// Response body returned after successful authentication.
/// </summary>
public sealed class TokenResponse
{
    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the token type.
    /// </summary>
    public required string TokenType { get; set; }

    /// <summary>
    /// Gets or sets the token expiration timestamp.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the authenticated user profile.
    /// </summary>
    public required UserTokenProfile User { get; set; }
}
