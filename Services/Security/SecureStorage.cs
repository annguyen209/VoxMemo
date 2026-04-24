using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace VoxMemo.Services.Security;

public static class SecureStorage
{
    private static readonly byte[] EntropyBytes = Encoding.UTF8.GetBytes("VoxMemo_SecureStorage_v1");

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = ProtectedData.Protect(
                plainBytes,
                EntropyBytes,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SecureStorage: Failed to encrypt data");
            return string.Empty;
        }
    }

    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;

        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedText);
            var decryptedBytes = ProtectedData.Unprotect(
                encryptedBytes,
                EntropyBytes,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SecureStorage: Failed to decrypt data");
            return string.Empty;
        }
    }

    public static bool IsEncrypted(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        try
        {
            Convert.FromBase64String(value);
            return value.Length >= 24;
        }
        catch
        {
            return false;
        }
    }
}