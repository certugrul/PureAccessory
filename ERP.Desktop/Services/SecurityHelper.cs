using System;
using System.Security.Cryptography;
using System.Text;

namespace ERP.Desktop.Services;

/// <summary>
/// Hassas bilgilerin (SMTP parolaları vb.) Windows DPAPI (Data Protection API)
/// ile işletim sistemi seviyesinde güvenli şekilde şifrelenmesini ve çözülmesini sağlar.
/// Düz metin (plaintext) parola saklanmasını engeller.
/// </summary>
public static class SecurityHelper
{
    private static readonly byte[] Entropy = { 65, 114, 112, 83, 101, 99, 117, 114, 101, 80, 97, 115, 115 };

    public static string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipherBytes);
        }
        catch
        {
            // Fallback: Standart Base64 (DPAPI desteklenmeyen nadir durumlarda)
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }
    }

    public static string DecryptString(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return "";
        try
        {
            byte[] cipherBytes = Convert.FromBase64String(cipherText);
            byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(cipherText));
            }
            catch
            {
                return cipherText;
            }
        }
    }
}
