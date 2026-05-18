using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RestLib.Abstractions;
using RestLib.Sample.Ecommerce;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Storefront;

/// <summary>
/// Maps custom storefront account endpoints.
/// </summary>
public static class StorefrontAccountEndpoints
{
    /// <summary>
    /// Maps the authenticated storefront account endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapStorefrontAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/storefront")
            .RequireAuthorization("Customer")
            .WithTags("Storefront Account");

        group.MapGet("/me", GetMeAsync)
            .RequireRateLimiting(EcommerceRateLimitPolicies.StorefrontRead)
            .WithSummary("Get my storefront profile")
            .WithDescription("Returns the current customer's profile derived from the JWT subject.");

        group.MapPatch("/me", PatchMeAsync)
            .RequireRateLimiting(EcommerceRateLimitPolicies.StorefrontWrite)
            .WithSummary("Patch my storefront profile")
            .WithDescription("Updates the current customer's username or email address derived from the JWT subject.");

        return endpoints;
    }

    private static async Task<IResult> GetMeAsync(
        ICurrentUser currentUser,
        EcommerceDbContext db,
        IRestLibMapper<UserDto, User> mapper,
        CancellationToken ct)
    {
        var user = await FindCurrentCustomerAsync(currentUser, db, ct);
        return user is null
            ? Results.NotFound()
            : Results.Ok(mapper.ToApi(user));
    }

    private static async Task<IResult> PatchMeAsync(
        UpdateProfileRequest request,
        ICurrentUser currentUser,
        EcommerceDbContext db,
        IRestLibMapper<UserDto, User> mapper,
        CancellationToken ct)
    {
        var user = await FindCurrentCustomerAsync(currentUser, db, ct);
        if (user is null)
        {
            return Results.NotFound();
        }

        var validationErrors = ValidateUpdateProfileRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var normalizedUserName = request.UserName?.Trim().ToUpperInvariant();
        var normalizedEmail = request.Email?.Trim().ToUpperInvariant();
        var hasDuplicate = await db.Users.AnyAsync(
            candidate => candidate.Id != user.Id
                && ((normalizedUserName != null && candidate.UserName.ToUpper() == normalizedUserName)
                    || (normalizedEmail != null && candidate.Email.ToUpper() == normalizedEmail)),
            ct);

        if (hasDuplicate)
        {
            return Results.Conflict(new { error = "user_already_exists" });
        }

        if (request.UserName is not null)
        {
            user.UserName = request.UserName.Trim();
        }

        if (request.Email is not null)
        {
            user.Email = request.Email.Trim();
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(mapper.ToApi(user));
    }

    private static async Task<User?> FindCurrentCustomerAsync(
        ICurrentUser currentUser,
        EcommerceDbContext db,
        CancellationToken ct)
    {
        return currentUser.UserId is { } userId
            ? await db.Users.SingleOrDefaultAsync(
                user => user.Id == userId && user.Role == "Customer" && user.IsActive,
                ct)
            : null;
    }

    private static Dictionary<string, string[]> ValidateUpdateProfileRequest(UpdateProfileRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.UserName is null && request.Email is null)
        {
            errors["request"] = ["At least one profile field is required."];
        }

        if (request.UserName is not null && string.IsNullOrWhiteSpace(request.UserName))
        {
            errors[nameof(UpdateProfileRequest.UserName)] = ["Username cannot be empty."];
        }

        if (request.UserName is { Length: > 100 })
        {
            errors[nameof(UpdateProfileRequest.UserName)] = ["Username cannot exceed 100 characters."];
        }

        if (request.Email is not null && string.IsNullOrWhiteSpace(request.Email))
        {
            errors[nameof(UpdateProfileRequest.Email)] = ["Email cannot be empty."];
        }

        if (request.Email is { Length: > 200 })
        {
            errors[nameof(UpdateProfileRequest.Email)] = ["Email cannot exceed 200 characters."];
        }

        if (request.Email is not null && !request.Email.Contains('@', StringComparison.Ordinal))
        {
            errors[nameof(UpdateProfileRequest.Email)] = ["Email must contain an at-sign."];
        }

        return errors;
    }
}
