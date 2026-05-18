using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Auth;

/// <summary>
/// Issues JWT access tokens for ecommerce sample users.
/// </summary>
public sealed class JwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly SigningCredentials _signingCredentials;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtTokenService"/> class.
    /// </summary>
    /// <param name="settings">The JWT settings.</param>
    public JwtTokenService(JwtSettings settings)
    {
        _settings = settings;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(settings.GetSigningKeyBytes()),
            SecurityAlgorithms.HmacSha256);
    }

    /// <summary>
    /// Creates an access token for the supplied user.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <returns>The token response.</returns>
    public TokenResponse CreateToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_settings.TokenLifetimeMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("role", user.Role),
            new("user_id", user.Id.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new TokenResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            TokenType = "Bearer",
            ExpiresAt = expiresAt,
            User = new UserTokenProfile
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Role = user.Role,
            },
        };
    }
}
