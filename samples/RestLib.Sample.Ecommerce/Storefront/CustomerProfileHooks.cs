using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Hooks;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Storefront;

/// <summary>
/// Named hooks for customer-owned profile resources.
/// </summary>
public static class CustomerProfileHooks
{
    /// <summary>
    /// The named hook used by customer profile resources to keep one primary row.
    /// </summary>
    public const string EnsureSinglePrimaryHookName = "EnsureSinglePrimary";

    /// <summary>
    /// Ensures a customer address belongs to the current customer and is the only primary address when marked primary.
    /// </summary>
    /// <param name="context">The RestLib hook context.</param>
    /// <returns>A task that completes when the hook work is done.</returns>
    public static async Task EnsureSinglePrimaryAddressAsync(HookContext<Address, Guid> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Entity is not { } address || !TryGetCustomerId(context, out var customerId))
        {
            return;
        }

        var now = DateTime.UtcNow;
        ApplyAddressOwnership(address, context.OriginalEntity, customerId, now, context.Operation);

        if (address.IsPrimary)
        {
            var db = context.Services.GetRequiredService<EcommerceDbContext>();
            await db.Addresses
                .Where(candidate => candidate.CustomerId == customerId
                    && candidate.Id != address.Id
                    && candidate.IsPrimary)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.IsPrimary, false)
                        .SetProperty(candidate => candidate.UpdatedAt, now),
                    context.CancellationToken);
        }

        if (context.Operation == RestLibOperation.Patch)
        {
            var db = context.Services.GetRequiredService<EcommerceDbContext>();
            await db.SaveChangesAsync(context.CancellationToken);
        }
    }

    /// <summary>
    /// Ensures a customer phone belongs to the current customer and is the only primary phone when marked primary.
    /// </summary>
    /// <param name="context">The RestLib hook context.</param>
    /// <returns>A task that completes when the hook work is done.</returns>
    public static async Task EnsureSinglePrimaryPhoneAsync(HookContext<Phone, Guid> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Entity is not { } phone || !TryGetCustomerId(context, out var customerId))
        {
            return;
        }

        ApplyPhoneOwnership(phone, context.OriginalEntity, customerId, DateTime.UtcNow, context.Operation);

        if (phone.IsPrimary)
        {
            var db = context.Services.GetRequiredService<EcommerceDbContext>();
            await db.Phones
                .Where(candidate => candidate.CustomerId == customerId
                    && candidate.Id != phone.Id
                    && candidate.IsPrimary)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(candidate => candidate.IsPrimary, false),
                    context.CancellationToken);
        }

        if (context.Operation == RestLibOperation.Patch)
        {
            var db = context.Services.GetRequiredService<EcommerceDbContext>();
            await db.SaveChangesAsync(context.CancellationToken);
        }
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

    private static void ApplyAddressOwnership(
        Address address,
        Address? original,
        Guid customerId,
        DateTime now,
        RestLibOperation operation)
    {
        if (operation == RestLibOperation.Create && address.Id == Guid.Empty)
        {
            address.Id = Guid.NewGuid();
        }

        address.CustomerId = customerId;

        if (operation == RestLibOperation.Create)
        {
            address.CreatedAt = now;
            address.UpdatedAt = null;
            return;
        }

        address.CreatedAt = original?.CreatedAt ?? address.CreatedAt;
        address.UpdatedAt = now;
    }

    private static void ApplyPhoneOwnership(
        Phone phone,
        Phone? original,
        Guid customerId,
        DateTime now,
        RestLibOperation operation)
    {
        if (operation == RestLibOperation.Create && phone.Id == Guid.Empty)
        {
            phone.Id = Guid.NewGuid();
        }

        phone.CustomerId = customerId;

        if (operation == RestLibOperation.Create)
        {
            phone.CreatedAt = now;
            return;
        }

        phone.CreatedAt = original?.CreatedAt ?? phone.CreatedAt;
    }
}
