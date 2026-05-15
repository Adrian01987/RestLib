using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestLib.Responses;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;
using RestLib.Sample.Ecommerce.Payments;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Maps custom storefront order command endpoints.
/// </summary>
public static class StorefrontOrderEndpoints
{
    private const string DefaultStatus = "PLACED";
    private const string DeliveryConfirmedStatus = "DELIVERY CONFIRMED";
    private const string PaidStatus = "PAID";

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

        group.MapPost("/{id:guid}/pay", PayAsync)
            .RequireAuthorization("Customer")
            .WithSummary("Pay for an order")
            .WithDescription("Pays an authenticated customer's order through the payment strategy matching the order payment method.");

        return group;
    }

    private static async Task<IResult> PayAsync(
        Guid id,
        HttpContext httpContext,
        ICurrentUser currentUser,
        EcommerceDbContext db,
        IPaymentStrategyResolver paymentStrategyResolver,
        IDomainEventDispatcher domainEventDispatcher,
        CancellationToken ct)
    {
        if (currentUser is not { IsCustomer: true, UserId: { } customerId })
        {
            return Results.Forbid();
        }

        var order = await db.Orders
            .Include(candidate => candidate.Payment)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id && candidate.CustomerId == customerId,
                ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.PaidAt is not null || string.Equals(order.Payment?.Status, PaidStatus, StringComparison.Ordinal))
        {
            return PaymentAlreadyProcessedProblem(order, httpContext.Request.Path.ToString());
        }

        var currentStatus = OrderHooks.NormalizeStatus(order.Status, DefaultStatus);
        if (!OrderHooks.CanTransition(currentStatus, PaidStatus))
        {
            return ProblemDetailsResult.InvalidStatusTransition(
                currentStatus,
                PaidStatus,
                httpContext.Request.Path.ToString());
        }

        if (!paymentStrategyResolver.TryResolve(order, out var paymentStrategy))
        {
            return UnsupportedPaymentMethodProblem(order.PaymentMethod, httpContext.Request.Path.ToString());
        }

        var paymentResult = await paymentStrategy.ProcessAsync(order, ct);
        if (!paymentResult.Succeeded)
        {
            return PaymentFailedProblem(order, paymentResult, httpContext.Request.Path.ToString());
        }

        var now = DateTime.UtcNow;
        var payment = order.Payment ?? new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Method = paymentStrategy.Method,
            Status = PaidStatus,
            Amount = order.Total,
        };

        payment.Method = paymentStrategy.Method;
        payment.Status = PaidStatus;
        payment.Amount = order.Total;
        payment.ExternalReference = paymentResult.ExternalReference;
        payment.PaidAt = now;

        if (order.Payment is null)
        {
            db.Payments.Add(payment);
            order.Payment = payment;
        }

        order.Status = PaidStatus;
        order.PaidAt = now;
        order.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        await domainEventDispatcher.DispatchAsync(new OrderPaid
        {
            OrderId = order.Id,
            PaymentId = payment.Id,
            CustomerId = order.CustomerId,
            PaymentMethod = payment.Method,
            ExternalReference = payment.ExternalReference ?? string.Empty,
            OrderStatus = order.Status,
            Total = order.Total,
            OccurredAt = now,
        }, ct);

        return Results.Ok(PaymentResponse.From(order, payment));
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

    private static IResult PaymentAlreadyProcessedProblem(Order order, string? instance)
    {
        return ProblemDetailsResult.Create(new RestLibProblemDetails
        {
            Type = "/problems/payment-already-processed",
            Title = "Payment Already Processed",
            Status = StatusCodes.Status409Conflict,
            Detail = $"Order '{order.Id}' has already been paid.",
            Instance = instance,
            Extensions = CreateExtensions(
                ("order_id", order.Id),
                ("payment_method", order.PaymentMethod)),
        });
    }

    private static IResult PaymentFailedProblem(Order order, PaymentStrategyResult paymentResult, string? instance)
    {
        var errorCode = NormalizePaymentErrorCode(paymentResult.ErrorCode);
        return ProblemDetailsResult.Create(new RestLibProblemDetails
        {
            Type = $"/problems/{errorCode}",
            Title = "Payment Failed",
            Status = StatusCodes.Status402PaymentRequired,
            Detail = paymentResult.ErrorMessage ?? "The payment strategy failed to process the order.",
            Instance = instance,
            Extensions = CreateExtensions(
                ("error_code", errorCode),
                ("order_id", order.Id),
                ("payment_method", order.PaymentMethod),
                ("amount", order.Total)),
        });
    }

    private static IResult UnsupportedPaymentMethodProblem(string paymentMethod, string? instance)
    {
        return ProblemDetailsResult.Create(new RestLibProblemDetails
        {
            Type = "/problems/unsupported-payment-method",
            Title = "Unsupported Payment Method",
            Status = StatusCodes.Status400BadRequest,
            Detail = $"Payment method '{paymentMethod}' is not supported.",
            Instance = instance,
            Extensions = CreateExtensions(("payment_method", paymentMethod)),
        });
    }

    private static IDictionary<string, JsonElement> CreateExtensions(
        params (string Key, object? Value)[] values)
    {
        var extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            extensions[key] = JsonSerializer.SerializeToElement(value);
        }

        return extensions;
    }

    private static string NormalizePaymentErrorCode(string? errorCode)
    {
        return string.IsNullOrWhiteSpace(errorCode)
            ? "payment_failed"
            : errorCode.Trim().ToLowerInvariant();
    }
}

/// <summary>
/// Response body returned after a successful order payment.
/// </summary>
public sealed class PaymentResponse
{
    /// <summary>
    /// Gets or sets the order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the payment identifier.
    /// </summary>
    public Guid PaymentId { get; set; }

    /// <summary>
    /// Gets or sets the order status after payment.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the payment method.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the payment completion timestamp.
    /// </summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// Gets or sets the external payment reference.
    /// </summary>
    public string? ExternalReference { get; set; }

    /// <summary>
    /// Gets or sets the order total.
    /// </summary>
    public decimal Total { get; set; }

    internal static PaymentResponse From(Order order, Payment payment)
    {
        return new PaymentResponse
        {
            OrderId = order.Id,
            PaymentId = payment.Id,
            Status = order.Status,
            PaymentMethod = payment.Method,
            PaidAt = order.PaidAt,
            ExternalReference = payment.ExternalReference,
            Total = order.Total,
        };
    }
}
