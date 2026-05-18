namespace RestLib.Sample.Ecommerce.Admin;

/// <summary>
/// Request body for administrator-driven carrier provisioning.
/// </summary>
public sealed class CarrierProvisioningRequest
{
    /// <summary>
    /// Gets or sets the carrier login username.
    /// </summary>
    public required string UserName { get; set; }

    /// <summary>
    /// Gets or sets the carrier login email address.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the carrier login password.
    /// </summary>
    public required string Password { get; set; }

    /// <summary>
    /// Gets or sets the carrier display name.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the carrier service area.
    /// </summary>
    public required string ServiceArea { get; set; }
}
