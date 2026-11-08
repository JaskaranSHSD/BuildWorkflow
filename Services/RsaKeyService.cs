using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace BuildWorkflow.Services;

public sealed class RsaKeyService
{
    private readonly RSA _rsa;
    private readonly string _keyPath;
    private readonly ILogger<RsaKeyService>? _logger;

    public RsaKeyService(IWebHostEnvironment environment, ILogger<RsaKeyService>? logger = null)
    {
        _logger = logger;
        _keyPath = Path.Combine(environment.ContentRootPath, "Data", "rsa_keys");
        Directory.CreateDirectory(_keyPath);
        _rsa = LoadOrCreateKeys();
    }

    private RSA LoadOrCreateKeys()
    {
        var privateKeyPath = Path.Combine(_keyPath, "private_key.pem");
        var publicKeyPath = Path.Combine(_keyPath, "public_key.pem");

        if (File.Exists(privateKeyPath) && File.Exists(publicKeyPath))
        {
            try
            {
                var rsa = RSA.Create();
                var privateKeyPem = File.ReadAllText(privateKeyPath);
                rsa.ImportFromPem(privateKeyPem);
                _logger?.LogInformation("Loaded existing RSA keys from {Path}", _keyPath);
                return rsa;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load existing RSA keys, generating new ones");
            }
        }

        // Generate new keys
        var newRsa = RSA.Create(2048);
        try
        {
            var privateKeyPem = newRsa.ExportRSAPrivateKeyPem();
            var publicKeyPem = newRsa.ExportSubjectPublicKeyInfoPem();
            File.WriteAllText(privateKeyPath, privateKeyPem);
            File.WriteAllText(publicKeyPath, publicKeyPem);
            _logger?.LogInformation("Generated and saved new RSA keys to {Path}", _keyPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save RSA keys");
        }
        return newRsa;
    }

    public string GetPublicKeyBase64() =>
        Convert.ToBase64String(_rsa.ExportSubjectPublicKeyInfo());

    public string Decrypt(string cipherBase64)
    {
        var cipher = Convert.FromBase64String(cipherBase64);
        var plain = _rsa.Decrypt(cipher, RSAEncryptionPadding.OaepSHA256);
        return System.Text.Encoding.UTF8.GetString(plain);
    }
}