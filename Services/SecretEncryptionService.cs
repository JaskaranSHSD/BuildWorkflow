using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace BuildWorkflow.Services;

public interface ISecretEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

public sealed class SecretEncryptionService : ISecretEncryptionService
{
    private readonly byte[] _key;
    private readonly ILogger<SecretEncryptionService>? _logger;

    public SecretEncryptionService(IConfiguration configuration, ILogger<SecretEncryptionService>? logger = null)
    {
        _logger = logger;

        // Get or generate encryption key from configuration
        var keyBase64 = configuration["Encryption:Key"];
        if (!string.IsNullOrWhiteSpace(keyBase64))
        {
            try
            {
                _key = Convert.FromBase64String(keyBase64);
                if (_key.Length != 32)
                {
                    throw new InvalidOperationException("Encryption key must be 32 bytes (256 bits) for AES-256");
                }
            }
            catch
            {
                throw new InvalidOperationException("Invalid encryption key format. Must be Base64-encoded 32-byte key.");
            }
        }
        else
        {
            // Generate a key and warn - in production, this should be configured
            using var rng = RandomNumberGenerator.Create();
            _key = new byte[32];
            rng.GetBytes(_key);
            _logger?.LogWarning("No encryption key configured! Generated ephemeral key. Secrets will be unreadable after restart. Set Encryption:Key in configuration.");
        }
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Combine IV + ciphertext
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        try
        {
            var fullBytes = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // Extract IV (first 16 bytes)
            var iv = new byte[16];
            Buffer.BlockCopy(fullBytes, 0, iv, 0, 16);
            aes.IV = iv;

            // Extract ciphertext
            var cipherBytes = new byte[fullBytes.Length - 16];
            Buffer.BlockCopy(fullBytes, 16, cipherBytes, 0, cipherBytes.Length);

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to decrypt secret");
            throw new InvalidOperationException("Failed to decrypt secret. Key may have changed or data is corrupted.");
        }
    }
}