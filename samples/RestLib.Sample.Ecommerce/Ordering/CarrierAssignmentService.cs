using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestLib.Abstractions;
using RestLib.Pagination;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Assigns carriers to newly placed orders.
/// </summary>
public interface ICarrierAssignmentService
{
    /// <summary>
    /// Assigns a carrier for a placed order.
    /// </summary>
    /// <param name="domainEvent">The order placed event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when assignment finishes.</returns>
    Task AssignCarrierAsync(OrderPlaced domainEvent, CancellationToken ct);
}

/// <summary>
/// Handles <see cref="OrderPlaced"/> events by assigning a carrier.
/// </summary>
public sealed class OrderPlacedHandler : IDomainEventHandler<OrderPlaced>
{
    private readonly ICarrierAssignmentService _carrierAssignmentService;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderPlacedHandler"/> class.
    /// </summary>
    /// <param name="carrierAssignmentService">The carrier assignment service.</param>
    public OrderPlacedHandler(ICarrierAssignmentService carrierAssignmentService)
    {
        _carrierAssignmentService = carrierAssignmentService;
    }

    /// <inheritdoc />
    public Task HandleAsync(OrderPlaced domainEvent, CancellationToken ct)
    {
        return _carrierAssignmentService.AssignCarrierAsync(domainEvent, ct);
    }
}

/// <summary>
/// Round-robin carrier assignment service for the ecommerce sample.
/// </summary>
public sealed class CarrierAssignmentService : ICarrierAssignmentService
{
    private const string AssignedStatus = "ASSIGNED";

    private readonly IRepository<Carrier, Guid> _carrierRepository;
    private readonly CarrierAssignmentCursor _cursor;
    private readonly EcommerceDbContext _db;
    private readonly INotificationService _notificationService;
    private readonly ILogger<CarrierAssignmentService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CarrierAssignmentService"/> class.
    /// </summary>
    /// <param name="carrierRepository">The carrier repository.</param>
    /// <param name="cursor">The round-robin cursor.</param>
    /// <param name="db">The ecommerce database context.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="logger">The logger.</param>
    public CarrierAssignmentService(
        IRepository<Carrier, Guid> carrierRepository,
        CarrierAssignmentCursor cursor,
        EcommerceDbContext db,
        INotificationService notificationService,
        ILogger<CarrierAssignmentService> logger)
    {
        _carrierRepository = carrierRepository;
        _cursor = cursor;
        _db = db;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AssignCarrierAsync(OrderPlaced domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var carrier = await SelectCarrierAsync(ct);
        if (carrier is null)
        {
            _logger.LogWarning(
                "Order {OrderId} was placed but no active carriers are available for assignment.",
                domainEvent.OrderId);
            return;
        }

        var shipment = await _db.Shipments
            .IgnoreQueryFilters()
            .Include(candidate => candidate.Order)
            .SingleOrDefaultAsync(candidate => candidate.Id == domainEvent.ShipmentId, ct);
        if (shipment is null)
        {
            _logger.LogWarning(
                "Order {OrderId} was placed but shipment {ShipmentId} was not found.",
                domainEvent.OrderId,
                domainEvent.ShipmentId);
            return;
        }

        var order = shipment.Order
            ?? await _db.Orders
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(candidate => candidate.Id == domainEvent.OrderId, ct);
        if (order is null)
        {
            _logger.LogWarning(
                "Shipment {ShipmentId} was found but order {OrderId} was not found.",
                domainEvent.ShipmentId,
                domainEvent.OrderId);
            return;
        }

        if (!OrderHooks.CanTransition(order.Status, AssignedStatus))
        {
            _logger.LogWarning(
                "Order {OrderId} could not transition from {CurrentStatus} to {TargetStatus}.",
                order.Id,
                order.Status,
                AssignedStatus);
            return;
        }

        var now = DateTime.UtcNow;
        shipment.CarrierId = carrier.UserId;
        shipment.Status = AssignedStatus;
        order.Status = AssignedStatus;
        order.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Assigned order {OrderId} shipment {ShipmentId} to carrier {CarrierId}.",
            order.Id,
            shipment.Id,
            carrier.UserId);

        await NotifyCustomerAsync(domainEvent, carrier, now, ct);
        await NotifyCarrierAsync(domainEvent, carrier, now, ct);
    }

    private async Task<Carrier?> SelectCarrierAsync(CancellationToken ct)
    {
        var page = await _carrierRepository.GetAllAsync(new PaginationRequest { Limit = 100 }, ct);
        var activeCarriers = page.Items
            .Where(carrier => carrier.IsActive && carrier.UserId != Guid.Empty)
            .OrderBy(carrier => carrier.DisplayName, StringComparer.Ordinal)
            .ThenBy(carrier => carrier.Id)
            .ToArray();

        if (activeCarriers.Length == 0)
        {
            return null;
        }

        var index = _cursor.NextIndex(activeCarriers.Length);
        return activeCarriers[index];
    }

    private Task NotifyCustomerAsync(
        OrderPlaced domainEvent,
        Carrier carrier,
        DateTime occurredAt,
        CancellationToken ct)
    {
        return _notificationService.NotifyAsync(new NotificationMessage
        {
            Kind = "order_assigned_customer",
            RecipientRole = "Customer",
            RecipientUserId = domainEvent.CustomerId,
            OrderId = domainEvent.OrderId,
            ShipmentId = domainEvent.ShipmentId,
            CarrierUserId = carrier.UserId,
            CarrierDisplayName = carrier.DisplayName,
            OrderStatus = AssignedStatus,
            ShipmentStatus = AssignedStatus,
            OccurredAt = occurredAt,
        }, ct);
    }

    private Task NotifyCarrierAsync(
        OrderPlaced domainEvent,
        Carrier carrier,
        DateTime occurredAt,
        CancellationToken ct)
    {
        return _notificationService.NotifyAsync(new NotificationMessage
        {
            Kind = "order_assigned_carrier",
            RecipientRole = "Carrier",
            RecipientUserId = carrier.UserId,
            OrderId = domainEvent.OrderId,
            ShipmentId = domainEvent.ShipmentId,
            CarrierUserId = carrier.UserId,
            CarrierDisplayName = carrier.DisplayName,
            OrderStatus = AssignedStatus,
            ShipmentStatus = AssignedStatus,
            OccurredAt = occurredAt,
        }, ct);
    }
}

/// <summary>
/// Stores round-robin carrier assignment state.
/// </summary>
public sealed class CarrierAssignmentCursor
{
    private int _nextIndex = -1;

    /// <summary>
    /// Gets the next carrier index for the specified carrier count.
    /// </summary>
    /// <param name="carrierCount">The number of selectable carriers.</param>
    /// <returns>The selected carrier index.</returns>
    public int NextIndex(int carrierCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(carrierCount);

        var next = Interlocked.Increment(ref _nextIndex);
        return (int)((uint)next % (uint)carrierCount);
    }
}
