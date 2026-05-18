using System.Security.Cryptography;

namespace RestLib.Sample.Ecommerce.Auth;

/// <summary>
/// Hashes and verifies passwords using PBKDF2.
/// </summary>
public static class PasswordHasher
{
    private const int CurrentVersion = 1;
    private const int IterationCount = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    /// <summary>
    /// Hashes a plaintext password.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <returns>The encoded password hash.</returns>
    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            IterationCount,
            HashAlgorithmName.SHA256,
            KeySize);

        return string.Join(
            ':',
            CurrentVersion,
            IterationCount,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    /// <summary>
    /// Verifies a plaintext password against an encoded hash.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="encodedHash">The encoded hash.</param>
    /// <returns><c>true</c> when the password matches; otherwise, <c>false</c>.</returns>
    public static bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split(':');
        if (parts.Length != 4
            || !int.TryParse(parts[0], out var version)
            || version != CurrentVersion
            || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
