using System.Security.Cryptography;
using System.Text;

namespace AviSite.Web.Security;

/// <summary>
/// PBKDF2-SHA256 password hashes in the form <c>pbkdf2-sha256$iterations$salt$hash</c> (base64).
/// Generate one with: <c>dotnet run --project src/AviSite.Web -- hash-password</c>
/// </summary>
public static class AdminPassword
{
    private const string Prefix = "pbkdf2-sha256";
    private const int Iterations = 600_000; // OWASP 2023 guidance for PBKDF2-HMAC-SHA256

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations) || iterations < 100_000) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
