using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RestLib;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Hooks;
using RestLib.Sample.Ecommerce;
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
            .RequireRateLimiting(EcommerceRateLimitPolicies.AdminBatch)
            .WithSummary("Patch admin order status")
            .WithDescription("Patches an order status after validating the status state machine in a BeforePersist hook.");

        return group;
    }

    private static async Task<IResult> PatchStatusAsync(
        Guid id,
        OrderStatusPatchRequest request,
        HttpContext httpContext,
        EcommerceDbContext db,
        IETagGenerator etagGenerator,
        CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(candidate => candidate.Id == id, ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (httpContext.Request.Headers.IfMatch.Count == 0)
        {
            return PreconditionRequired();
        }

        var currentETag = etagGenerator.Generate(order);
        if (!ETagComparer.IfMatchSucceeds(httpContext.Request.Headers.IfMatch, currentETag))
        {
            return PreconditionFailed();
        }

        var candidate = new Order
        {
            Id = order.Id,
            CustomerId = order.CustomerId,
            Status = request.Status,
            PaymentMethod = order.PaymentMethod,
            Total = order.Total,
            RowVersion = order.RowVersion,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            PaidAt = order.PaidAt,
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PreconditionFailed();
        }

        httpContext.Response.Headers.ETag = etagGenerator.Generate(order);

        return Results.Ok(order);
    }

    private static IResult PreconditionRequired()
    {
        return Results.Problem(
            type: "/problems/precondition-required",
            title: "Precondition Required",
            statusCode: StatusCodes.Status428PreconditionRequired,
            detail: "If-Match header is required to patch an order status.");
    }

    private static IResult PreconditionFailed()
    {
        return Results.Problem(
            type: "/problems/precondition-failed",
            title: "Precondition Failed",
            statusCode: StatusCodes.Status412PreconditionFailed,
            detail: "The resource has been modified since you last retrieved it.");
    }
}
