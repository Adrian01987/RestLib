using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib;
using RestLib.Hooks;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Typed hooks for storefront and admin order resources.
/// </summary>
public static class OrderHooks
{
    /// <summary>
    /// The hook item key used by custom status endpoints to provide the original status.
    /// </summary>
    public const string OriginalStatusItemKey = "OriginalStatus";

    private const string DefaultStatus = "PLACED";

    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["PLACED"] = ["ASSIGNED", "PAID", "CANCELLED"],
            ["ASSIGNED"] = ["PAID", "CANCELLED"],
            ["PAID"] = ["ON THE WAY", "CANCELLED"],
            ["ON THE WAY"] = ["DELIVERED", "NOT DELIVERED"],
            ["NOT DELIVERED"] = ["ON THE WAY", "CANCELLED"],
            ["DELIVERED"] = [],
            ["CANCELLED"] = [],
        };

    /// <summary>
    /// Applies admin-order defaults before standard request validation runs.
    /// </summary>
    /// <param name="context">The RestLib hook context.</param>
    /// <returns>A completed task.</returns>
    public static Task ApplyAdminOrderDefaultsAsync(HookContext<Order, Guid> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Operation == RestLibOperation.Create && context.Entity is { } order)
        {
            ApplyCreationDefaults(order);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Prepares an admin order before persistence and validates status transitions.
    /// </summary>
    /// <param name="context">The RestLib hook context.</param>
    /// <returns>A completed task.</returns>
    public static Task PrepareAdminOrderAsync(HookContext<Order, Guid> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Entity is not { } order)
        {
            return Task.CompletedTask;
        }

        if (context.Operation is not RestLibOperation.Create and not RestLibOperation.Update and not RestLibOperation.Patch)
        {
            return Task.CompletedTask;
        }

        if (context.Operation == RestLibOperation.Create)
        {
            ApplyCreationDefaults(order);
            return ValidateOrderShapeAsync(context, order);
        }

        var originalStatus = GetOriginalStatus(context);
        var targetStatus = NormalizeStatus(order.Status, originalStatus ?? DefaultStatus);
        order.Status = targetStatus;
        order.UpdatedAt = DateTime.UtcNow;

        if (originalStatus is not null && !CanTransition(originalStatus, targetStatus))
        {
            StopWithInvalidTransition(context, originalStatus, targetStatus);
            return Task.CompletedTask;
        }

        return ValidateOrderShapeAsync(context, order);
    }

    /// <summary>
    /// Ensures storefront-created orders belong to the current customer and start in PLACED status.
    /// </summary>
    /// <param name="context">The RestLib hook context.</param>
    /// <returns>A completed task.</returns>
    public static Task PrepareStorefrontOrderAsync(HookContext<Order, Guid> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Operation != RestLibOperation.Create || context.Entity is not { } order)
        {
            return Task.CompletedTask;
        }

        var currentUser = context.Services.GetRequiredService<ICurrentUser>();
        if (currentUser is not { IsCustomer: true, UserId: { } customerId })
        {
            context.ShouldContinue = false;
            context.EarlyResult = Results.Forbid();
            return Task.CompletedTask;
        }

        ApplyCreationDefaults(order);
        order.CustomerId = customerId;
        order.Status = DefaultStatus;
        return ValidateOrderShapeAsync(context, order);
    }

    /// <summary>
    /// Determines whether an order can move from the current status to the target status.
    /// </summary>
    /// <param name="currentStatus">The current order status.</param>
    /// <param name="targetStatus">The target order status.</param>
    /// <returns><see langword="true"/> when the transition is allowed.</returns>
    public static bool CanTransition(string currentStatus, string targetStatus)
    {
        var current = NormalizeStatus(currentStatus, DefaultStatus);
        var target = NormalizeStatus(targetStatus, DefaultStatus);

        if (!AllowedTransitions.ContainsKey(target))
        {
            return false;
        }

        return string.Equals(current, target, StringComparison.Ordinal)
            || (AllowedTransitions.TryGetValue(current, out var allowed)
                && allowed.Contains(target, StringComparer.Ordinal));
    }

    /// <summary>
    /// Normalizes an order status for persistence and transition checks.
    /// </summary>
    /// <param name="status">The candidate status.</param>
    /// <param name="fallback">The fallback status when the candidate is empty.</param>
    /// <returns>The normalized status.</returns>
    public static string NormalizeStatus(string? status, string fallback)
    {
        return string.IsNullOrWhiteSpace(status)
            ? fallback
            : status.Trim().ToUpperInvariant();
    }

    private static Task ValidateOrderShapeAsync(HookContext<Order, Guid> context, Order order)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!AllowedTransitions.ContainsKey(order.Status))
        {
            errors[nameof(Order.Status)] = [$"Unsupported order status '{order.Status}'."];
        }

        if (string.IsNullOrWhiteSpace(order.PaymentMethod))
        {
            errors[nameof(Order.PaymentMethod)] = ["Payment method is required."];
        }

        if (order.Total < 0)
        {
            errors[nameof(Order.Total)] = ["Total cannot be negative."];
        }

        if (errors.Count > 0)
        {
            context.ShouldContinue = false;
            context.EarlyResult = Results.ValidationProblem(errors);
        }

        return Task.CompletedTask;
    }

    private static void ApplyCreationDefaults(Order order)
    {
        if (order.Id == Guid.Empty)
        {
            order.Id = Guid.NewGuid();
        }

        order.Status = NormalizeStatus(order.Status, DefaultStatus);
        if (order.CreatedAt == default)
        {
            order.CreatedAt = DateTime.UtcNow;
        }

        order.UpdatedAt = null;
    }

    private static string? GetOriginalStatus(HookContext<Order, Guid> context)
    {
        if (context.OriginalEntity is { } original)
        {
            return NormalizeStatus(original.Status, DefaultStatus);
        }

        return context.Items.TryGetValue(OriginalStatusItemKey, out var value) && value is string status
            ? NormalizeStatus(status, DefaultStatus)
            : null;
    }

    private static void StopWithInvalidTransition(
        HookContext<Order, Guid> context,
        string originalStatus,
        string targetStatus)
    {
        context.ShouldContinue = false;
        context.EarlyResult = Results.Conflict(new
        {
            error = "invalid_status_transition",
            from = originalStatus,
            to = targetStatus,
        });
    }
}
