using Microsoft.Extensions.Logging;
using RestLib.Sample.Ecommerce.Ordering;

namespace RestLib.Sample.Ecommerce.Fulfillment;

/// <summary>
/// Handles <see cref="ShipmentStatusChanged"/> events by notifying the customer.
/// </summary>
public sealed class ShipmentStatusChangedHandler : IDomainEventHandler<ShipmentStatusChanged>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<ShipmentStatusChangedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShipmentStatusChangedHandler"/> class.
    /// </summary>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="logger">The logger.</param>
    public ShipmentStatusChangedHandler(
        INotificationService notificationService,
        ILogger<ShipmentStatusChangedHandler> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ShipmentStatusChanged domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _logger.LogInformation(
            "Shipment {ShipmentId} for order {OrderId} changed to {ShipmentStatus}.",
            domainEvent.ShipmentId,
            domainEvent.OrderId,
            domainEvent.ShipmentStatus);

        await _notificationService.NotifyAsync(new NotificationMessage
        {
            Kind = "shipment_status_changed_customer",
            RecipientRole = "Customer",
            RecipientUserId = domainEvent.CustomerId,
            OrderId = domainEvent.OrderId,
            ShipmentId = domainEvent.ShipmentId,
            CarrierUserId = domainEvent.CarrierUserId,
            CarrierDisplayName = domainEvent.CarrierDisplayName,
            OrderStatus = domainEvent.OrderStatus,
            ShipmentStatus = domainEvent.ShipmentStatus,
            OccurredAt = domainEvent.OccurredAt,
        }, ct);
    }
}
