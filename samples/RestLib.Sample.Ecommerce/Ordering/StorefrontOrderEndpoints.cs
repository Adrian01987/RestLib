using Microsoft.EntityFrameworkCore;
using RestLib.Responses;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Identity;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Maps custom storefront order command endpoints.
/// </summary>
public static class StorefrontOrderEndpoints
{
    private const string DefaultStatus = "PLACED";
    private const string DeliveryConfirmedStatus = "DELIVERY CONFIRMED";

    /// <summary>
    /// Maps custom storefront order commands onto the storefront orders route group.
    /// </summary>
    /// <param name="group">The storefront orders route group.</param>
    /// <returns>The storefront orders route group for chaining.</returns>
    public static RouteGroupBuilder MapStorefrontOrderCommands(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/{id:guid}/confirm-delivery", ConfirmDeliveryAsync)
            .RequireAuthorization("Customer")
            .WithSummary("Confirm order delivery")
            .WithDescription("Confirms delivery for an authenticated customer's delivered order.");

        return group;
    }

    private static async Task<IResult> ConfirmDeliveryAsync(
        Guid id,
        HttpContext httpContext,
        ICurrentUser currentUser,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        if (currentUser is not { IsCustomer: true, UserId: { } customerId })
        {
            return Results.Forbid();
        }

        var order = await db.Orders.SingleOrDefaultAsync(
            candidate => candidate.Id == id && candidate.CustomerId == customerId,
            ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        var currentStatus = OrderHooks.NormalizeStatus(order.Status, DefaultStatus);
        if (!OrderHooks.CanTransition(currentStatus, DeliveryConfirmedStatus))
        {
            return ProblemDetailsResult.InvalidStatusTransition(
                currentStatus,
                DeliveryConfirmedStatus,
                httpContext.Request.Path.ToString());
        }

        order.Status = DeliveryConfirmedStatus;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(order);
    }
}
