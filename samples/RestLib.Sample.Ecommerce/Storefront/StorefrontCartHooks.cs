using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestLib;
using RestLib.Hooks;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Storefront;

/// <summary>
/// Named hooks and persistence helpers for storefront cart resources.
/// </summary>
public static class StorefrontCartHooks
{
    /// <summary>
    /// The named hook used to own and constrain active customer carts.
    /// </summary>
    public const string EnsureActiveCartHookName = "EnsureActiveCart";

    /// <summary>
    /// The named hook used to prepare cart items before persistence.
    /// </summary>
    public const string PrepareCartItemHookName = "PrepareCartItem";

    /// <summary>
    /// Ensures a cart belongs to the current customer and does not create a second active cart.
    /// </summary>
    /// <param name="context">The RestLib hook context.</param>
    /// <returns>A task that completes when the hook work is done.</returns>
    public static async Task EnsureActiveCartAsync(HookContext<Cart, Guid> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Entity is not { } cart || !TryGetCustomerId(context, out var customerId))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (context.Operation == RestLibOperation.Create && cart.Id == Guid.Empty)
        {
            cart.Id = Guid.NewGuid();
        }

        cart.CustomerId = customerId;
        cart.Status = NormalizeStatus(cart.Status);
        cart.CreatedAt = context.Operation == RestLibOperation.Create
            ? now
            : context.OriginalEntity?.CreatedAt ?? cart.CreatedAt;

        if (!string.Equals(cart.Status, "ACTIVE", StringComparison.Ordinal))
        {
            return;
        }

        var db = context.Services.GetRequiredService<EcommerceDbContext>();
        var hasActiveCart = await db.Carts
            .IgnoreQueryFilters()
            .AnyAsync(
                candidate => candidate.CustomerId == customerId
                    && candidate.Id != cart.Id
                    && candidate.Status == "ACTIVE",
                context.CancellationToken);

        if (hasActiveCart)
        {
            context.ShouldContinue = false;
            context.EarlyResult = Results.Conflict(new { error = "active_cart_already_exists" });
        }
    }

    /// <summary>
    /// Prepares a cart item before persistence by reading product stock and recomputing totals.
    /// </summary>
    /// <param name="context">The RestLib hook context.</param>
    /// <returns>A task that completes when the hook work is done.</returns>
    public static async Task PrepareCartItemAsync(HookContext<CartItem, RestLibCompositeKey<Guid, Guid>> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Entity is not { } item)
        {
            return;
        }

        var db = context.Services.GetRequiredService<EcommerceDbContext>();
        var result = await PrepareCartItemAsync(item, db, context.CancellationToken);
        if (result is not null)
        {
            context.ShouldContinue = false;
            context.EarlyResult = result;
        }
    }

    /// <summary>
    /// Prepares a cart item before persistence by reading product stock and recomputing totals.
    /// </summary>
    /// <param name="item">The cart item to prepare.</param>
    /// <param name="db">The ecommerce database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>An error result when validation fails; otherwise <see langword="null"/>.</returns>
    public static async Task<IResult?> PrepareCartItemAsync(
        CartItem item,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(db);

        var validationErrors = ValidateCartItem(item);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var product = await db.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == item.ProductId && candidate.IsActive, ct);

        if (product is null)
        {
            return Results.NotFound(new { error = "product_not_found" });
        }

        if (item.Quantity > product.StockOnHand)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(CartItem.Quantity)] = [$"Quantity cannot exceed available stock ({product.StockOnHand})."]
            });
        }

        item.UnitPrice = product.Price;
        item.LineTotal = item.Quantity * item.UnitPrice;
        return null;
    }

    private static bool TryGetCustomerId<TEntity>(
        HookContext<TEntity, Guid> context,
        out Guid customerId)
        where TEntity : class
    {
        var currentUser = context.Services.GetRequiredService<ICurrentUser>();
        if (currentUser is { IsCustomer: true, UserId: { } userId })
        {
            customerId = userId;
            return true;
        }

        customerId = Guid.Empty;
        context.ShouldContinue = false;
        context.EarlyResult = Results.Forbid();
        return false;
    }

    private static Dictionary<string, string[]> ValidateCartItem(CartItem item)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (item.ProductId == Guid.Empty)
        {
            errors[nameof(CartItem.ProductId)] = ["Product id is required."];
        }

        if (item.Quantity <= 0)
        {
            errors[nameof(CartItem.Quantity)] = ["Quantity must be greater than zero."];
        }

        return errors;
    }

    private static string NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? "ACTIVE"
            : status.Trim().ToUpperInvariant();
    }
}
