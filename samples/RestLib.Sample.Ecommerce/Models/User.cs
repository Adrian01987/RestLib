namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents an actor that can use the ecommerce sample API.
/// </summary>
public class User
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the login name.
    /// </summary>
    public required string UserName { get; set; }

    /// <summary>
    /// Gets or sets the user's email address.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the password hash.
    /// </summary>
    public required string PasswordHash { get; set; }

    /// <summary>
    /// Gets or sets the role name.
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user can sign in.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last successful login timestamp.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Gets the customer's addresses.
    /// </summary>
    public List<Address> Addresses { get; } = [];

    /// <summary>
    /// Gets the customer's phone numbers.
    /// </summary>
    public List<Phone> Phones { get; } = [];

    /// <summary>
    /// Gets the customer's carts.
    /// </summary>
    public List<Cart> Carts { get; } = [];

    /// <summary>
    /// Gets the customer's orders.
    /// </summary>
    public List<Order> Orders { get; } = [];

    /// <summary>
    /// Gets the shipments assigned to the user when they are a carrier.
    /// </summary>
    public List<Shipment> AssignedShipments { get; } = [];
}
