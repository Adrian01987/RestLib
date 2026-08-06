using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Responses;
using RestLib.Sample.Ecommerce.Models;
using RestLib.Sample.Ecommerce.Payments;

namespace RestLib.Sample.Ecommerce.Responses;

/// <summary>
/// Defines invariant metadata for an ecommerce Problem Details type.
/// </summary>
/// <param name="Type">The relative problem type URI.</param>
/// <param name="Title">The human-readable problem title.</param>
/// <param name="Status">The HTTP status code.</param>
internal readonly record struct EcommerceProblemDescriptor(string Type, string Title, int Status);

/// <summary>
/// Owns the ecommerce sample's domain-specific Problem Details metadata.
/// </summary>
internal static class EcommerceProblemCatalog
{
    internal static readonly EcommerceProblemDescriptor InsufficientStock = new(
        "/problems/insufficient-stock",
        "Insufficient Stock",
        StatusCodes.Status409Conflict);

    internal static readonly EcommerceProblemDescriptor InvalidStatusTransition = new(
        "/problems/invalid-status-transition",
        "Invalid Status Transition",
        StatusCodes.Status409Conflict);

    internal static readonly EcommerceProblemDescriptor PaymentAlreadyProcessed = new(
        "/problems/payment-already-processed",
        "Payment Already Processed",
        StatusCodes.Status409Conflict);

    internal static readonly EcommerceProblemDescriptor UnsupportedPaymentMethod = new(
        "/problems/unsupported-payment-method",
        "Unsupported Payment Method",
        StatusCodes.Status400BadRequest);

    internal static EcommerceProblemDescriptor PaymentFailed(string errorCode)
    {
        return new EcommerceProblemDescriptor(
            $"/problems/{errorCode}",
            "Payment Failed",
            StatusCodes.Status402PaymentRequired);
    }
}

/// <summary>
/// Creates ecommerce-domain Problem Details results through RestLib's generic result seam.
/// </summary>
internal static class EcommerceProblemResults
{
    internal static IResult InsufficientStock(
        HttpContext httpContext,
        CartItem cartItem,
        Product? product,
        int available)
    {
        var productName = product?.Name ?? "Unknown product";
        return Create(
            httpContext,
            EcommerceProblemCatalog.InsufficientStock,
            $"Product '{productName}' has {available} units available; requested {cartItem.Quantity}.",
            ("product_id", cartItem.ProductId.ToString("D")),
            ("requested", cartItem.Quantity),
            ("available", available));
    }

    internal static IResult InvalidStatusTransition(
        HttpContext httpContext,
        string fromStatus,
        string toStatus)
    {
        return Create(
            httpContext,
            EcommerceProblemCatalog.InvalidStatusTransition,
            $"Status cannot transition from '{fromStatus}' to '{toStatus}'.",
            ("from", fromStatus),
            ("to", toStatus));
    }

    internal static IResult PaymentAlreadyProcessed(HttpContext httpContext, Order order)
    {
        return Create(
            httpContext,
            EcommerceProblemCatalog.PaymentAlreadyProcessed,
            $"Order '{order.Id}' has already been paid.",
            ("order_id", order.Id),
            ("payment_method", order.PaymentMethod));
    }

    internal static IResult PaymentFailed(
        HttpContext httpContext,
        Order order,
        PaymentStrategyResult paymentResult)
    {
        var errorCode = NormalizePaymentErrorCode(paymentResult.ErrorCode);
        return Create(
            httpContext,
            EcommerceProblemCatalog.PaymentFailed(errorCode),
            paymentResult.ErrorMessage ?? "The payment strategy failed to process the order.",
            ("error_code", errorCode),
            ("order_id", order.Id),
            ("payment_method", order.PaymentMethod),
            ("amount", order.Total));
    }

    internal static IResult UnsupportedPaymentMethod(HttpContext httpContext, string paymentMethod)
    {
        return Create(
            httpContext,
            EcommerceProblemCatalog.UnsupportedPaymentMethod,
            $"Payment method '{paymentMethod}' is not supported.",
            ("payment_method", paymentMethod));
    }

    private static IResult Create(
        HttpContext httpContext,
        EcommerceProblemDescriptor descriptor,
        string detail,
        params (string Key, object? Value)[] extensionValues)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in extensionValues)
        {
            extensions[key] = JsonSerializer.SerializeToElement(value);
        }

        return ProblemDetailsResult.Create(new RestLibProblemDetails
        {
            Type = descriptor.Type,
            Title = descriptor.Title,
            Status = descriptor.Status,
            Detail = detail,
            Instance = httpContext.Request.Path.ToString(),
            Extensions = extensions,
        });
    }

    private static string NormalizePaymentErrorCode(string? errorCode)
    {
        return string.IsNullOrWhiteSpace(errorCode)
            ? "payment_failed"
            : errorCode.Trim().ToLowerInvariant();
    }
}
