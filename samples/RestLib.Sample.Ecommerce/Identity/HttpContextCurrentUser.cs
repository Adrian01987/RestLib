using System.Security.Claims;

namespace RestLib.Sample.Ecommerce.Identity;

/// <summary>
/// Resolves the current actor from <see cref="HttpContext.User"/>.
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpContextCurrentUser"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            var rawValue = CurrentPrincipal?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? CurrentPrincipal?.FindFirstValue("sub")
                ?? CurrentPrincipal?.FindFirstValue("user_id");

            return Guid.TryParse(rawValue, out var userId) ? userId : null;
        }
    }

    /// <inheritdoc />
    public string? Role => CurrentPrincipal?.FindFirstValue(ClaimTypes.Role)
        ?? CurrentPrincipal?.FindFirstValue("role");

    /// <inheritdoc />
    public bool IsAuthenticated => CurrentPrincipal?.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public bool IsAdmin => HasRole("Admin");

    /// <inheritdoc />
    public bool IsCustomer => HasRole("Customer");

    /// <inheritdoc />
    public bool IsCarrier => HasRole("Carrier");

    private ClaimsPrincipal? CurrentPrincipal => _httpContextAccessor.HttpContext?.User;

    private bool HasRole(string role)
    {
        return CurrentPrincipal?.IsInRole(role) == true
            || string.Equals(Role, role, StringComparison.OrdinalIgnoreCase);
    }
}
