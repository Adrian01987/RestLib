using Microsoft.Extensions.Logging;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Handles <see cref="OrderPaid"/> events by emitting a customer notification.
/// </summary>
public sealed class OrderPaidHandler : IDomainEventHandler<OrderPaid>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<OrderPaidHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderPaidHandler"/> class.
    /// </summary>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="logger">The logger.</param>
    public OrderPaidHandler(
        INotificationService notificationService,
        ILogger<OrderPaidHandler> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(OrderPaid domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _logger.LogInformation(
            "Order {OrderId} was paid with {PaymentMethod}; payment {PaymentId}.",
            domainEvent.OrderId,
            domainEvent.PaymentMethod,
            domainEvent.PaymentId);

        await _notificationService.NotifyAsync(new NotificationMessage
        {
            Kind = "order_paid_customer",
            RecipientRole = "Customer",
            RecipientUserId = domainEvent.CustomerId,
            OrderId = domainEvent.OrderId,
            ShipmentId = Guid.Empty,
            CarrierUserId = Guid.Empty,
            CarrierDisplayName = string.Empty,
            OrderStatus = domainEvent.OrderStatus,
            ShipmentStatus = string.Empty,
            OccurredAt = domainEvent.OccurredAt,
        }, ct);
    }
}
