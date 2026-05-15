using Microsoft.EntityFrameworkCore;
using RestLib.Responses;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Maps storefront checkout endpoints.
/// </summary>
public static class CheckoutEndpoints
{
    private const string PlacedStatus = "PLACED";
    private const string CheckedOutStatus = "CHECKED_OUT";

    /// <summary>
    /// Maps checkout onto the storefront order route group.
    /// </summary>
    /// <param name="group">The storefront order route group.</param>
    /// <returns>The storefront order route group for chaining.</returns>
    public static RouteGroupBuilder MapStorefrontCheckout(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/checkout", CheckoutAsync)
            .RequireAuthorization("Customer")
            .WithSummary("Checkout active cart")
            .WithDescription("Creates an order and shipment from the active cart inside one EF Core transaction.");

        return group;
    }

    private static async Task<IResult> CheckoutAsync(
        CheckoutRequest request,
        HttpContext httpContext,
        ICurrentUser currentUser,
        EcommerceDbContext db,
        IDomainEventDispatcher domainEventDispatcher,
        CancellationToken ct)
    {
        if (currentUser is not { IsCustomer: true, UserId: { } customerId })
        {
            return Results.Forbid();
        }

        var paymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod)
            ? "card"
            : request.PaymentMethod.Trim();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var cart = await db.Carts
            .Include(candidate => candidate.Items)
            .SingleOrDefaultAsync(
                candidate => candidate.CustomerId == customerId && candidate.Status == "ACTIVE",
                ct);

        if (cart is null)
        {
            await transaction.RollbackAsync(ct);
            return Results.NotFound(new { error = "active_cart_not_found" });
        }

        if (cart.Items.Count == 0)
        {
            await transaction.RollbackAsync(ct);
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["cart"] = ["The active cart has no items."]
            });
        }

        var productIds = cart.Items
            .Select(item => item.ProductId)
            .Distinct()
            .ToArray();
        var productsById = await db.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, ct);

        var now = DateTime.UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Status = PlacedStatus,
            PaymentMethod = paymentMethod,
            CreatedAt = now,
        };

        foreach (var cartItem in cart.Items)
        {
            if (!productsById.TryGetValue(cartItem.ProductId, out var product) || !product.IsActive)
            {
                await transaction.RollbackAsync(ct);
                return InsufficientStockProblem(cartItem, product, available: 0, httpContext.Request.Path.ToString());
            }

            if (cartItem.Quantity > product.StockOnHand)
            {
                await transaction.RollbackAsync(ct);
                return InsufficientStockProblem(cartItem, product, product.StockOnHand, httpContext.Request.Path.ToString());
            }

            var unitPrice = cartItem.UnitPrice > 0 ? cartItem.UnitPrice : product.Price;
            var lineTotal = cartItem.Quantity * unitPrice;
            product.StockOnHand -= cartItem.Quantity;
            order.Total += lineTotal;
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = cartItem.Quantity,
                UnitPrice = unitPrice,
                LineTotal = lineTotal,
            });
        }

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Status = PlacedStatus,
            CreatedAt = now,
        };

        order.Shipment = shipment;
        cart.Status = CheckedOutStatus;
        db.Orders.Add(order);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await domainEventDispatcher.DispatchAsync(new OrderPlaced
        {
            OrderId = order.Id,
            CustomerId = customerId,
            ShipmentId = shipment.Id,
            Total = order.Total,
            OccurredAt = DateTime.UtcNow,
        }, ct);

        return Results.Created(
            $"/api/storefront/orders/{order.Id}",
            CheckoutResponse.FromOrder(order, shipment.Id));
    }

    private static IResult InsufficientStockProblem(
        CartItem cartItem,
        Product? product,
        int available,
        string? instance)
    {
        var productName = product?.Name ?? "Unknown product";
        return ProblemDetailsResult.InsufficientStock(
            $"Product '{productName}' has {available} units available; requested {cartItem.Quantity}.",
            cartItem.ProductId.ToString("D"),
            cartItem.Quantity,
            available,
            instance);
    }
}
