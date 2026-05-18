using System.Text.Json;
using RestLib.Serialization;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Dispatches sample notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Dispatches a notification.
    /// </summary>
    /// <param name="message">The notification message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the notification is dispatched.</returns>
    Task NotifyAsync(NotificationMessage message, CancellationToken ct);
}

/// <summary>
/// Represents a notification emitted by the ecommerce sample.
/// </summary>
public sealed class NotificationMessage
{
    /// <summary>
    /// Gets the structured event type.
    /// </summary>
    public string EventType { get; init; } = "notification_sent";

    /// <summary>
    /// Gets the notification kind.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the role receiving the notification.
    /// </summary>
    public required string RecipientRole { get; init; }

    /// <summary>
    /// Gets the user identifier receiving the notification.
    /// </summary>
    public Guid RecipientUserId { get; init; }

    /// <summary>
    /// Gets the order identifier.
    /// </summary>
    public Guid OrderId { get; init; }

    /// <summary>
    /// Gets the shipment identifier.
    /// </summary>
    public Guid ShipmentId { get; init; }

    /// <summary>
    /// Gets the assigned carrier user identifier.
    /// </summary>
    public Guid CarrierUserId { get; init; }

    /// <summary>
    /// Gets the assigned carrier display name.
    /// </summary>
    public required string CarrierDisplayName { get; init; }

    /// <summary>
    /// Gets the order status after the notification-triggering change.
    /// </summary>
    public required string OrderStatus { get; init; }

    /// <summary>
    /// Gets the shipment status after the notification-triggering change.
    /// </summary>
    public required string ShipmentStatus { get; init; }

    /// <summary>
    /// Gets the notification timestamp.
    /// </summary>
    public DateTime OccurredAt { get; init; }
}

/// <summary>
/// Writes sample notifications to stdout as structured JSON lines.
/// </summary>
public sealed class ConsoleNotificationService : INotificationService
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    /// <inheritdoc />
    public Task NotifyAsync(NotificationMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ct.ThrowIfCancellationRequested();

        var line = JsonSerializer.Serialize(message, JsonOptions);
        return Console.Out.WriteLineAsync(line);
    }
}
