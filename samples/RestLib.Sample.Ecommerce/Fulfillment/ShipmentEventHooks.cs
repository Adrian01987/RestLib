using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestLib;
using RestLib.Abstractions;
using RestLib.Hooks;
using RestLib.Responses;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;
using RestLib.Sample.Ecommerce.Ordering;

namespace RestLib.Sample.Ecommerce.Fulfillment;

/// <summary>
/// Named hooks for carrier shipment event resources.
/// </summary>
public static class ShipmentEventHooks
{
    /// <summary>
    /// The named hook used to bind a shipment event to the route shipment and validate append permissions.
    /// </summary>
    public const string PrepareShipmentEventHookName = "PrepareShipmentEvent";

    /// <summary>
    /// The named hook used to propagate an appended shipment event to parent state.
    /// </summary>
    public const string PropagateShipmentEventHookName = "PropagateShipmentEvent";

    private const string PlacedStatus = "PLACED";
    private const string PropagationStateItemKey = "ShipmentEventPropagationState";

    private static readonly HashSet<string> AllowedShipmentStatuses = new(StringComparer.Ordinal)
    {
        "ASSIGNED",
        "ON THE WAY",
        "DELIVERED",
        "NOT DELIVERED",
    };

    /// <summary>
    /// Binds a create request to the route shipment and validates carrier ownership before persistence.
    /// </summary>
    /// <param name="context">The RestLib hook context.</param>
    /// <returns>A task that completes when the hook work is done.</returns>
    public static async Task PrepareShipmentEventAsync(HookContext<ShipmentEvent, Guid> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Operation != RestLibOperation.Create || context.Entity is not { } shipmentEvent)
        {
            return;
        }

        var currentUser = context.Services.GetRequiredService<ICurrentUser>();
        if (currentUser is not { IsCarrier: true, UserId: { } carrierUserId })
        {
            Stop(context, Results.Forbid());
            return;
        }

        if (!TryGetShipmentId(context.HttpContext, out var shipmentId))
        {
            Stop(context, Results.BadRequest(new { error = "invalid_shipment_id" }));
            return;
        }

        var shipmentStatus = NormalizeShipmentStatus(shipmentEvent.Status);
        if (!AllowedShipmentStatuses.Contains(shipmentStatus))
        {
            Stop(context, Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(ShipmentEvent.Status)] =
                [
                    "Status must be one of: ASSIGNED, ON THE WAY, DELIVERED, NOT DELIVERED."
                ],
            }));
            return;
        }

        var db = context.Services.GetRequiredService<EcommerceDbContext>();
        var shipment = await db.Shipments
            .SingleOrDefaultAsync(candidate => candidate.Id == shipmentId, context.CancellationToken);
        if (shipment is null)
        {
            Stop(context, Results.NotFound());
            return;
        }

        var order = await db.Orders
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.Id == shipment.OrderId, context.CancellationToken);
        if (order is null)
        {
            Stop(context, Results.NotFound(new { error = "order_not_found" }));
            return;
        }

        var orderStatus = MapOrderStatus(shipmentStatus);
        var currentOrderStatus = OrderHooks.NormalizeStatus(order.Status, PlacedStatus);
        if (!OrderHooks.CanTransition(currentOrderStatus, orderStatus))
        {
            Stop(context, ProblemDetailsResult.InvalidStatusTransition(
                currentOrderStatus,
                orderStatus,
                context.HttpContext.Request.Path.ToString()));
            return;
        }

        var occurredAt = DateTime.UtcNow;
        if (shipmentEvent.Id == Guid.Empty)
        {
            shipmentEvent.Id = Guid.NewGuid();
        }

        shipmentEvent.ShipmentId = shipmentId;
        shipmentEvent.Shipment = null;
        shipmentEvent.Status = shipmentStatus;
        shipmentEvent.OccurredAt = occurredAt;

        context.Items[PropagationStateItemKey] = new ShipmentEventPropagationState
        {
            OrderId = order.Id,
            ShipmentId = shipment.Id,
            CustomerId = order.CustomerId,
            CarrierUserId = carrierUserId,
            CarrierDisplayName = await ResolveCarrierDisplayNameAsync(context.Services, carrierUserId, context.CancellationToken),
            OrderStatus = orderStatus,
            ShipmentStatus = shipmentStatus,
            Notes = shipmentEvent.Notes,
            OccurredAt = occurredAt,
        };
    }

    /// <summary>
    /// Updates parent shipment and order state after a shipment event is persisted.
    /// </summary>
    /// <param name="context">The RestLib hook context.</param>
    /// <returns>A task that completes when the hook work is done.</returns>
    public static async Task PropagateShipmentEventAsync(HookContext<ShipmentEvent, Guid> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Operation != RestLibOperation.Create || context.Entity is not { } shipmentEvent)
        {
            return;
        }

        if (!TryGetPropagationState(context, out var state))
        {
            Stop(context, Results.StatusCode(StatusCodes.Status500InternalServerError));
            return;
        }

        var db = context.Services.GetRequiredService<EcommerceDbContext>();
        var shipment = await db.Shipments
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.Id == shipmentEvent.ShipmentId, context.CancellationToken);
        if (shipment is null)
        {
            Stop(context, Results.NotFound());
            return;
        }

        var order = await db.Orders
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.Id == state.OrderId, context.CancellationToken);
        if (order is null)
        {
            Stop(context, Results.NotFound(new { error = "order_not_found" }));
            return;
        }

        shipment.Status = state.ShipmentStatus;
        order.Status = state.OrderStatus;
        order.UpdatedAt = state.OccurredAt;

        await db.SaveChangesAsync(context.CancellationToken);

        var dispatcher = context.Services.GetRequiredService<IDomainEventDispatcher>();
        await dispatcher.DispatchAsync(new ShipmentStatusChanged
        {
            OrderId = state.OrderId,
            ShipmentId = state.ShipmentId,
            CustomerId = state.CustomerId,
            CarrierUserId = state.CarrierUserId,
            CarrierDisplayName = state.CarrierDisplayName,
            OrderStatus = state.OrderStatus,
            ShipmentStatus = state.ShipmentStatus,
            Notes = state.Notes,
            OccurredAt = state.OccurredAt,
        }, context.CancellationToken);

        db.Entry(shipmentEvent).State = EntityState.Detached;
        shipmentEvent.Shipment = null;
    }

    private static bool TryGetShipmentId(HttpContext httpContext, out Guid shipmentId)
    {
        if (httpContext.Request.RouteValues.TryGetValue("shipmentId", out var value)
            && Guid.TryParse(value?.ToString(), out shipmentId))
        {
            return true;
        }

        shipmentId = Guid.Empty;
        return false;
    }

    private static string NormalizeShipmentStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : status.Trim().ToUpperInvariant();
    }

    private static string MapOrderStatus(string shipmentStatus)
    {
        return shipmentStatus;
    }

    private static async Task<string> ResolveCarrierDisplayNameAsync(
        IServiceProvider services,
        Guid carrierUserId,
        CancellationToken ct)
    {
        var carrierRepository = services.GetRequiredService<IRepository<Carrier, Guid>>();
        var carrier = await carrierRepository.GetByIdAsync(carrierUserId, ct);
        return carrier?.DisplayName ?? string.Empty;
    }

    private static bool TryGetPropagationState(
        HookContext<ShipmentEvent, Guid> context,
        out ShipmentEventPropagationState state)
    {
        if (context.Items.TryGetValue(PropagationStateItemKey, out var value)
            && value is ShipmentEventPropagationState propagationState)
        {
            state = propagationState;
            return true;
        }

        state = new ShipmentEventPropagationState();
        return false;
    }

    private static void Stop(HookContext<ShipmentEvent, Guid> context, IResult result)
    {
        context.ShouldContinue = false;
        context.EarlyResult = result;
    }

    private sealed class ShipmentEventPropagationState
    {
        public Guid OrderId { get; init; }

        public Guid ShipmentId { get; init; }

        public Guid CustomerId { get; init; }

        public Guid CarrierUserId { get; init; }

        public string CarrierDisplayName { get; init; } = string.Empty;

        public string OrderStatus { get; init; } = string.Empty;

        public string ShipmentStatus { get; init; } = string.Empty;

        public string? Notes { get; init; }

        public DateTime OccurredAt { get; init; }
    }
}
