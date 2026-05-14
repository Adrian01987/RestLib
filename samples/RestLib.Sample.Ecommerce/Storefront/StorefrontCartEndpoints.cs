using Microsoft.EntityFrameworkCore;
using RestLib;
using RestLib.Hooks;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Storefront;

/// <summary>
/// Maps custom storefront cart endpoints.
/// </summary>
public static class StorefrontCartEndpoints
{
    /// <summary>
    /// Maps storefront cart item endpoints using the nested cart route.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapStorefrontCartEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/storefront/carts/{cartId:guid}/items")
            .RequireAuthorization("Customer")
            .WithTags("Storefront Cart");

        group.MapGet("", ListItemsAsync)
            .WithSummary("List cart items")
            .WithDescription("Lists items in the authenticated customer's active cart.");

        group.MapGet("/{productId:guid}", GetItemAsync)
            .WithSummary("Get cart item")
            .WithDescription("Gets one active cart item by the cart id and product id composite key.");

        group.MapPost("", CreateItemAsync)
            .WithSummary("Add cart item")
            .WithDescription("Adds a product to the authenticated customer's active cart.");

        group.MapPut("/{productId:guid}", ReplaceItemAsync)
            .WithSummary("Replace cart item")
            .WithDescription("Replaces the quantity of a product in the authenticated customer's active cart.");

        group.MapPatch("/{productId:guid}", PatchItemAsync)
            .WithSummary("Patch cart item")
            .WithDescription("Updates the quantity of a product in the authenticated customer's active cart.");

        group.MapDelete("/{productId:guid}", DeleteItemAsync)
            .WithSummary("Delete cart item")
            .WithDescription("Removes a product from the authenticated customer's active cart.");

        return endpoints;
    }

    private static async Task<IResult> ListItemsAsync(
        Guid cartId,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        var cart = await FindActiveCartAsync(cartId, db, ct);
        if (cart is null)
        {
            return Results.NotFound();
        }

        var items = await db.CartItems
            .AsNoTracking()
            .Where(item => item.CartId == cartId && item.Cart!.Status == "ACTIVE")
            .OrderBy(item => item.ProductId)
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetItemAsync(
        Guid cartId,
        Guid productId,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        var item = await FindActiveCartItemAsync(cartId, productId, db, ct);
        return item is null
            ? Results.NotFound()
            : Results.Ok(item);
    }

    private static async Task<IResult> CreateItemAsync(
        Guid cartId,
        CartItemCreateRequest request,
        HttpContext httpContext,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        var cart = await FindActiveCartAsync(cartId, db, ct);
        if (cart is null)
        {
            return Results.NotFound();
        }

        var exists = await db.CartItems.AnyAsync(
            item => item.CartId == cartId && item.ProductId == request.ProductId,
            ct);

        if (exists)
        {
            return Results.Conflict(new { error = "cart_item_already_exists" });
        }

        var item = new CartItem
        {
            CartId = cartId,
            ProductId = request.ProductId,
            Quantity = request.Quantity,
        };

        var beforePersistResult = await RunBeforePersistAsync(item, RestLibOperation.Create, httpContext, ct);
        if (beforePersistResult is not null)
        {
            return beforePersistResult;
        }

        db.CartItems.Add(item);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/storefront/carts/{cartId}/items/{item.ProductId}", item);
    }

    private static async Task<IResult> ReplaceItemAsync(
        Guid cartId,
        Guid productId,
        CartItemQuantityRequest request,
        HttpContext httpContext,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        var item = await FindActiveCartItemAsync(cartId, productId, db, ct);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.Quantity = request.Quantity;

        var beforePersistResult = await RunBeforePersistAsync(item, RestLibOperation.Update, httpContext, ct);
        if (beforePersistResult is not null)
        {
            return beforePersistResult;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(item);
    }

    private static async Task<IResult> PatchItemAsync(
        Guid cartId,
        Guid productId,
        CartItemQuantityRequest request,
        HttpContext httpContext,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        var item = await FindActiveCartItemAsync(cartId, productId, db, ct);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.Quantity = request.Quantity;

        var beforePersistResult = await RunBeforePersistAsync(item, RestLibOperation.Patch, httpContext, ct);
        if (beforePersistResult is not null)
        {
            return beforePersistResult;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(item);
    }

    private static async Task<IResult> DeleteItemAsync(
        Guid cartId,
        Guid productId,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        var item = await FindActiveCartItemAsync(cartId, productId, db, ct);
        if (item is null)
        {
            return Results.NotFound();
        }

        db.CartItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Task<Cart?> FindActiveCartAsync(
        Guid cartId,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        return db.Carts.SingleOrDefaultAsync(
            cart => cart.Id == cartId && cart.Status == "ACTIVE",
            ct);
    }

    private static Task<CartItem?> FindActiveCartItemAsync(
        Guid cartId,
        Guid productId,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        return db.CartItems.SingleOrDefaultAsync(
            item => item.CartId == cartId
                && item.ProductId == productId
                && item.Cart!.Status == "ACTIVE",
            ct);
    }

    private static async Task<IResult?> RunBeforePersistAsync(
        CartItem item,
        RestLibOperation operation,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var context = new HookContext<CartItem, RestLibCompositeKey<Guid, Guid>>
        {
            HttpContext = httpContext,
            Operation = operation,
            ResourceId = item.Id,
            Entity = item,
            Services = httpContext.RequestServices,
            CancellationToken = ct,
        };

        await StorefrontCartHooks.PrepareCartItemAsync(context);
        return context.ShouldContinue
            ? null
            : context.EarlyResult ?? Results.StatusCode(500);
    }
}

/// <summary>
/// Request body for adding a product to a cart.
/// </summary>
public sealed class CartItemCreateRequest
{
    /// <summary>
    /// Gets or sets the product identifier.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the desired quantity.
    /// </summary>
    public int Quantity { get; set; }
}

/// <summary>
/// Request body for changing a cart item quantity.
/// </summary>
public sealed class CartItemQuantityRequest
{
    /// <summary>
    /// Gets or sets the desired quantity.
    /// </summary>
    public int Quantity { get; set; }
}
