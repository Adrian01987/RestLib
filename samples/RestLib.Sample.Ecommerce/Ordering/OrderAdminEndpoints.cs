using Microsoft.EntityFrameworkCore;
using RestLib;
using RestLib.Hooks;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Maps custom admin order endpoints.
/// </summary>
public static class OrderAdminEndpoints
{
    /// <summary>
    /// Maps the admin status patch endpoint onto the admin orders route group.
    /// </summary>
    /// <param name="group">The admin orders route group.</param>
    /// <returns>The admin orders route group for chaining.</returns>
    public static RouteGroupBuilder MapAdminOrderStatusPatch(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPatch("/{id:guid}", PatchStatusAsync)
            .RequireAuthorization("Admin")
            .WithSummary("Patch admin order status")
            .WithDescription("Patches an order status after validating the status state machine in a BeforePersist hook.");

        return group;
    }

    private static async Task<IResult> PatchStatusAsync(
        Guid id,
        OrderStatusPatchRequest request,
        HttpContext httpContext,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(candidate => candidate.Id == id, ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        var candidate = new Order
        {
            Id = order.Id,
            CustomerId = order.CustomerId,
            Status = request.Status,
            PaymentMethod = order.PaymentMethod,
            Total = order.Total,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
        };

        var hookContext = new HookContext<Order, Guid>
        {
            HttpContext = httpContext,
            Operation = RestLibOperation.Patch,
            ResourceId = id,
            Entity = candidate,
            Services = httpContext.RequestServices,
            CancellationToken = ct,
        };
        hookContext.Items[OrderHooks.OriginalStatusItemKey] = order.Status;

        await OrderHooks.PrepareAdminOrderAsync(hookContext);
        if (!hookContext.ShouldContinue)
        {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        order.Status = candidate.Status;
        order.UpdatedAt = candidate.UpdatedAt;
        await db.SaveChangesAsync(ct);

        return Results.Ok(order);
    }
}
