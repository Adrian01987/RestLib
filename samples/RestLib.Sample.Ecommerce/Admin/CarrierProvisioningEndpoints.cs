using Microsoft.EntityFrameworkCore;
using RestLib.Abstractions;
using RestLib.Sample.Ecommerce.Auth;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Admin;

/// <summary>
/// Maps administrator endpoints for provisioning carrier accounts.
/// </summary>
public static class CarrierProvisioningEndpoints
{
    /// <summary>
    /// Maps carrier provisioning endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapCarrierProvisioningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/admin/carriers")
            .RequireAuthorization("Admin")
            .WithTags("Admin Carriers");

        group.MapPost("", ProvisionCarrierAsync)
            .WithSummary("Provision carrier")
            .WithDescription("Creates a carrier login user and its in-memory carrier reference row.");

        return endpoints;
    }

    private static async Task<IResult> ProvisionCarrierAsync(
        CarrierProvisioningRequest request,
        EcommerceDbContext db,
        IRepository<Carrier, Guid> carrierRepository,
        CancellationToken ct)
    {
        var validationErrors = ValidateCarrierProvisioningRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var normalizedUserName = request.UserName.Trim().ToUpperInvariant();
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        if (await db.Users.AnyAsync(
            user => user.UserName.ToUpper() == normalizedUserName || user.Email.ToUpper() == normalizedEmail,
            ct))
        {
            return Results.Conflict(new { error = "user_already_exists" });
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = request.UserName.Trim(),
            Email = request.Email.Trim(),
            PasswordHash = PasswordHasher.Hash(request.Password),
            Role = "Carrier",
            CreatedAt = now,
        };

        var carrier = new Carrier
        {
            Id = user.Id,
            UserId = user.Id,
            DisplayName = request.DisplayName.Trim(),
            ServiceArea = request.ServiceArea.Trim(),
            IsActive = true,
            CreatedAt = now,
        };

        db.Users.Add(user);
        await carrierRepository.CreateAsync(carrier, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await carrierRepository.DeleteAsync(carrier.Id, ct);
            throw;
        }

        return Results.Created($"/api/admin/carriers/{carrier.Id}", carrier);
    }

    private static Dictionary<string, string[]> ValidateCarrierProvisioningRequest(CarrierProvisioningRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            errors[nameof(CarrierProvisioningRequest.UserName)] = ["Username is required."];
        }
        else if (request.UserName.Length > 100)
        {
            errors[nameof(CarrierProvisioningRequest.UserName)] = ["Username cannot exceed 100 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors[nameof(CarrierProvisioningRequest.Email)] = ["Email is required."];
        }
        else if (request.Email.Length > 200)
        {
            errors[nameof(CarrierProvisioningRequest.Email)] = ["Email cannot exceed 200 characters."];
        }
        else if (!request.Email.Contains('@', StringComparison.Ordinal))
        {
            errors[nameof(CarrierProvisioningRequest.Email)] = ["Email must contain an at-sign."];
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors[nameof(CarrierProvisioningRequest.Password)] = ["Password is required."];
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            errors[nameof(CarrierProvisioningRequest.DisplayName)] = ["Display name is required."];
        }
        else if (request.DisplayName.Length > 120)
        {
            errors[nameof(CarrierProvisioningRequest.DisplayName)] = ["Display name cannot exceed 120 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.ServiceArea))
        {
            errors[nameof(CarrierProvisioningRequest.ServiceArea)] = ["Service area is required."];
        }
        else if (request.ServiceArea.Length > 120)
        {
            errors[nameof(CarrierProvisioningRequest.ServiceArea)] = ["Service area cannot exceed 120 characters."];
        }

        return errors;
    }
}
