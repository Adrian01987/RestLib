namespace RestLib.Sample.Ecommerce.Identity;

/// <summary>
/// Provides the current authenticated actor for request-scoped data access.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Gets the authenticated user identifier, when available.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// Gets the current user's role, when available.
    /// </summary>
    string? Role { get; }

    /// <summary>
    /// Gets a value indicating whether the request has an authenticated user.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets a value indicating whether the current user is an administrator.
    /// </summary>
    bool IsAdmin { get; }

    /// <summary>
    /// Gets a value indicating whether the current user is a customer.
    /// </summary>
    bool IsCustomer { get; }

    /// <summary>
    /// Gets a value indicating whether the current user is a carrier.
    /// </summary>
    bool IsCarrier { get; }
}
