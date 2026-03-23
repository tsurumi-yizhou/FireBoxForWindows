using System.Security.Cryptography;

namespace Service.Data;

/// <summary>
/// Encrypts/decrypts API keys using Windows DPAPI (per-user scope).
/// </summary>
public sealed class SecureKeyStore
{
    public byte[] Encrypt(string plainText)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
    }

    public string Decrypt(byte[] encryptedBytes)
    {
        if (encryptedBytes.Length == 0) return string.Empty;
        var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }
}
