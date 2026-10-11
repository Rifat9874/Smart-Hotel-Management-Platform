using System.Security.Cryptography;
using System.Text;

namespace HotelManagement.Web.Services;

/// <summary>
/// The WinForms app stored passwords as plain text. Here new passwords are stored as a salted
/// PBKDF2 hash. Old plain-text passwords (for example from your restored HMSDB1 database) still
/// work: they are accepted once and silently upgraded to a hash after a successful login.
/// </summary>
public static class PasswordService
{
    private const string Prefix = "$HMS1$";
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <returns>ok = password is correct; needsUpgrade = stored value was an old plain-text password.</returns>
    public static (bool ok, bool needsUpgrade) Verify(string stored, string provided)
    {
        if (string.IsNullOrEmpty(stored) || provided is null) return (false, false);

        if (stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var parts = stored.Substring(Prefix.Length).Split('$');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var iter)) return (false, false);
            try
            {
                var salt = Convert.FromBase64String(parts[1]);
                var expected = Convert.FromBase64String(parts[2]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(provided, salt, iter, HashAlgorithmName.SHA256, expected.Length);
                return (CryptographicOperations.FixedTimeEquals(actual, expected), false);
            }
            catch (FormatException)
            {
                return (false, false);
            }
        }

        // legacy plain-text password
        var a = Encoding.UTF8.GetBytes(stored);
        var b = Encoding.UTF8.GetBytes(provided);
        var same = a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        return (same, same);
    }
}
