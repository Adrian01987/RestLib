using System.Text;

namespace RestLib.Sample.Ecommerce.Auth;

/// <summary>
/// JWT and bootstrap settings for the ecommerce sample.
/// </summary>
public sealed class JwtSettings
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "RestLibSample:Auth";

    /// <summary>
    /// Gets or sets the token issuer.
    /// </summary>
    public string Issuer { get; set; } = "RestLib.Sample.Ecommerce";

    /// <summary>
    /// Gets or sets the token audience.
    /// </summary>
    public string Audience { get; set; } = "RestLib.Sample.Ecommerce";

    /// <summary>
    /// Gets or sets the symmetric signing key.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the first-admin bootstrap key.
    /// </summary>
    public string BootstrapKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token lifetime in minutes.
    /// </summary>
    public int TokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Loads and validates JWT settings from configuration.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The validated JWT settings.</returns>
    public static JwtSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration.GetSection(SectionName).Get<JwtSettings>() ?? new JwtSettings();
        settings.Validate();
        return settings;
    }

    /// <summary>
    /// Gets the signing key as UTF-8 bytes.
    /// </summary>
    /// <returns>The signing key bytes.</returns>
    public byte[] GetSigningKeyBytes()
    {
        return Encoding.UTF8.GetBytes(SigningKey);
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey) || GetSigningKeyBytes().Length < 32)
        {
            throw new InvalidOperationException(
                "RestLibSample:Auth:SigningKey must be configured with at least 32 UTF-8 bytes.");
        }

        if (string.IsNullOrWhiteSpace(BootstrapKey))
        {
            throw new InvalidOperationException("RestLibSample:Auth:BootstrapKey must be configured.");
        }

        if (TokenLifetimeMinutes <= 0)
        {
            throw new InvalidOperationException("RestLibSample:Auth:TokenLifetimeMinutes must be greater than 0.");
        }
    }
}
