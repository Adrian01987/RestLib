using Microsoft.EntityFrameworkCore;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Auth;

/// <summary>
/// Maps authentication endpoints for the ecommerce sample.
/// </summary>
public static class AuthEndpoints
{
    private const string BootstrapHeaderName = "X-Bootstrap-Key";

    /// <summary>
    /// Maps the sample authentication endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapEcommerceAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/auth")
            .WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithSummary("Login")
            .WithDescription("Issues a JWT for a seeded or registered ecommerce sample user.");

        group.MapPost("/admin-bootstrap", BootstrapAdminAsync)
            .AllowAnonymous()
            .WithSummary("Bootstrap the first administrator")
            .WithDescription("Creates the first administrator when the X-Bootstrap-Key header matches configuration.");

        group.MapPost("/register-customer", RegisterCustomerAsync)
            .AllowAnonymous()
            .WithSummary("Register a customer")
            .WithDescription("Creates a customer account and returns a JWT for storefront access.");

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        EcommerceDbContext db,
        JwtTokenService jwtTokenService,
        CancellationToken ct)
    {
        var validationErrors = ValidateLoginRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var userNameOrEmail = request.UserNameOrEmail.Trim();
        var normalized = userNameOrEmail.ToUpperInvariant();
        var user = await db.Users
            .SingleOrDefaultAsync(candidate =>
                candidate.UserName.ToUpper() == normalized || candidate.Email.ToUpper() == normalized,
                ct);

        if (user is null || !user.IsActive || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Results.Unauthorized();
        }

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(jwtTokenService.CreateToken(user));
    }

    private static async Task<IResult> BootstrapAdminAsync(
        AdminBootstrapRequest request,
        HttpContext httpContext,
        EcommerceDbContext db,
        JwtSettings jwtSettings,
        JwtTokenService jwtTokenService,
        CancellationToken ct)
    {
        if (!httpContext.Request.Headers.TryGetValue(BootstrapHeaderName, out var providedKey)
            || !string.Equals(providedKey.ToString(), jwtSettings.BootstrapKey, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        var validationErrors = ValidateAdminBootstrapRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        if (await db.Users.AnyAsync(user => user.Role == "Admin", ct))
        {
            return Results.Conflict(new { error = "admin_already_exists" });
        }

        var normalizedUserName = request.UserName.Trim().ToUpperInvariant();
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        if (await db.Users.AnyAsync(
            user => user.UserName.ToUpper() == normalizedUserName || user.Email.ToUpper() == normalizedEmail,
            ct))
        {
            return Results.Conflict(new { error = "user_already_exists" });
        }

        var admin = new User
        {
            Id = Guid.NewGuid(),
            UserName = request.UserName.Trim(),
            Email = request.Email.Trim(),
            PasswordHash = PasswordHasher.Hash(request.Password),
            Role = "Admin",
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/admin/users/{admin.Id}", jwtTokenService.CreateToken(admin));
    }

    private static async Task<IResult> RegisterCustomerAsync(
        RegisterCustomerRequest request,
        EcommerceDbContext db,
        JwtTokenService jwtTokenService,
        CancellationToken ct)
    {
        var validationErrors = ValidateRegisterCustomerRequest(request);
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
        var customer = new User
        {
            Id = Guid.NewGuid(),
            UserName = request.UserName.Trim(),
            Email = request.Email.Trim(),
            PasswordHash = PasswordHasher.Hash(request.Password),
            Role = "Customer",
            CreatedAt = now,
            LastLoginAt = now,
        };

        db.Users.Add(customer);
        await db.SaveChangesAsync(ct);

        return Results.Created("/api/storefront/me", jwtTokenService.CreateToken(customer));
    }

    private static Dictionary<string, string[]> ValidateLoginRequest(LoginRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.UserNameOrEmail))
        {
            errors[nameof(LoginRequest.UserNameOrEmail)] = ["Username or email is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors[nameof(LoginRequest.Password)] = ["Password is required."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateAdminBootstrapRequest(AdminBootstrapRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            errors[nameof(AdminBootstrapRequest.UserName)] = ["Username is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors[nameof(AdminBootstrapRequest.Email)] = ["Email is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors[nameof(AdminBootstrapRequest.Password)] = ["Password is required."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateRegisterCustomerRequest(RegisterCustomerRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            errors[nameof(RegisterCustomerRequest.UserName)] = ["Username is required."];
        }
        else if (request.UserName.Length > 100)
        {
            errors[nameof(RegisterCustomerRequest.UserName)] = ["Username cannot exceed 100 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors[nameof(RegisterCustomerRequest.Email)] = ["Email is required."];
        }
        else if (request.Email.Length > 200)
        {
            errors[nameof(RegisterCustomerRequest.Email)] = ["Email cannot exceed 200 characters."];
        }
        else if (!request.Email.Contains('@', StringComparison.Ordinal))
        {
            errors[nameof(RegisterCustomerRequest.Email)] = ["Email must contain an at-sign."];
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors[nameof(RegisterCustomerRequest.Password)] = ["Password is required."];
        }

        return errors;
    }
}
