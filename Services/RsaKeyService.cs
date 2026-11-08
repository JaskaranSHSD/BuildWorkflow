using System.Security.Cryptography;

namespace BuildWorkflow.Services;

public sealed class RsaKeyService
{
    private readonly RSA _rsa = RSA.Create(2048);

    public string GetPublicKeyBase64() =>
        Convert.ToBase64String(_rsa.ExportSubjectPublicKeyInfo());

    public string Decrypt(string cipherBase64)
    {
        var cipher = Convert.FromBase64String(cipherBase64);
        var plain = _rsa.Decrypt(cipher, RSAEncryptionPadding.OaepSHA256);
        return System.Text.Encoding.UTF8.GetString(plain);
    }
}
