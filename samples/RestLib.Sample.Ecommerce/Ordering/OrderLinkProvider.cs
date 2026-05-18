using RestLib.Hypermedia;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Adds storefront workflow links to order responses.
/// </summary>
public sealed class OrderLinkProvider : IHateoasLinkProvider<Order, Guid>
{
    private const string PlacedStatus = "PLACED";
    private const string AssignedStatus = "ASSIGNED";
    private const string DeliveredStatus = "DELIVERED";

    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderLinkProvider"/> class.
    /// </summary>
    /// <param name="currentUser">The current request actor.</param>
    public OrderLinkProvider(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, HateoasLink>? GetLinks(Order entity, Guid key)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (_currentUser is not { IsCustomer: true, UserId: { } userId } || userId != entity.CustomerId)
        {
            return null;
        }

        var links = new Dictionary<string, HateoasLink>(StringComparer.Ordinal);
        var status = OrderHooks.NormalizeStatus(entity.Status, PlacedStatus);
        var orderUrl = $"/api/storefront/orders/{key}";

        if (status == PlacedStatus)
        {
            links["cancel"] = new HateoasLink
            {
                Href = $"{orderUrl}/cancel",
                Method = "POST",
            };
        }

        if (status is PlacedStatus or AssignedStatus)
        {
            links["pay"] = new HateoasLink
            {
                Href = $"{orderUrl}/pay",
                Method = "POST",
            };
        }

        if (status == DeliveredStatus)
        {
            links["confirm-delivery"] = new HateoasLink
            {
                Href = $"{orderUrl}/confirm-delivery",
                Method = "POST",
            };
        }

        return links.Count == 0 ? null : links;
    }
}
