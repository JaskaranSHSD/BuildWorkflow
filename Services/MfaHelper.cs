using System;
using System.Security.Cryptography;
using System.Text;

namespace BuildWorkflow.Services;

public static class MfaHelper
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret(int length = 20)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return Base32Encode(bytes);
    }

    public static string Base32Encode(byte[] data)
    {
        if (data == null || data.Length == 0) return string.Empty;
        var output = new StringBuilder();
        int buffer = 0;
        int bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                var index = (buffer >> (bitsLeft - 5)) & 31;
                output.Append(Base32Alphabet[index]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
        {
            var index = (buffer << (5 - bitsLeft)) & 31;
            output.Append(Base32Alphabet[index]);
        }
        return output.ToString();
    }

    public static byte[] Base32Decode(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Array.Empty<byte>();
        input = input.Trim().Replace(" ", "").ToUpperInvariant();
        var bits = 0;
        var value = 0;
        var output = new System.Collections.Generic.List<byte>();
        foreach (var c in input)
        {
            if (c == '=') continue;
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0) continue;
            value = (value << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 255));
                bits -= 8;
            }
        }
        return output.ToArray();
    }

    public static string GenerateTotp(string secret, DateTimeOffset? time = null, int stepSeconds = 30, int digits = 6)
    {
        var timestamp = (time ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / stepSeconds;
        return GenerateHotp(secret, timestamp, digits);
    }

    public static string GenerateHotp(string secret, long counter, int digits = 6)
    {
        var key = Base32Decode(secret);
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[hash.Length - 1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) |
                     ((hash[offset + 1] & 0xFF) << 16) |
                     ((hash[offset + 2] & 0xFF) << 8) |
                     (hash[offset + 3] & 0xFF);
        var otp = binary % (int)Math.Pow(10, digits);
        return otp.ToString().PadLeft(digits, '0');
    }

    public static bool VerifyTotp(string secret, string code, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        for (var i = -window; i <= window; i++)
        {
            var time = DateTimeOffset.UtcNow.AddSeconds(i * 30);
            if (GenerateTotp(secret, time) == code.Trim()) return true;
        }
        return false;
    }

    public static string PlatformCode(DateTimeOffset? time = null)
    {
        var now = time ?? DateTimeOffset.UtcNow;
        return (now.Year % 100).ToString("D2") + now.Day.ToString("D2");
    }
}
