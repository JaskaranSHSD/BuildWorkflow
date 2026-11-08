using BCrypt.Net;

namespace BuildWorkflow.Services;

public static class PasswordHasher
{
    private const int WorkFactor = 12;

    public static string Hash(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    public static bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
            return false;
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }

    public static bool NeedsRehash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return true;
        try
        {
            // BCrypt.Net-Next doesn't have PasswordNeedsRehash, check if hash was created with different work factor
            return false;
        }
        catch
        {
            return true;
        }
    }
}