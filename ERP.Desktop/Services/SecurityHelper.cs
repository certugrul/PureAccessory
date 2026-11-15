using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ERP.Desktop.Services;

/// <summary>
/// Hassas bilgilerin (SMTP parolaları vb.) hem yerel makinede hem de bulut senkronizasyonunda
/// tüm şube bilgisayarları arasında güvenli şekilde AES-256 ile şifrelenmesini ve çözülmesini sağlar.
/// </summary>
public static class SecurityHelper
{
    private static readonly byte[] Entropy = { 65, 114, 112, 83, 101, 99, 117, 114, 101, 80, 97, 115, 115 };
    private static readonly byte[] AesKey = SHA256.HashData(Encoding.UTF8.GetBytes("PureAccessoryERP_SmtpSecretKey_2026!"));
    private static readonly byte[] AesIv = MD5.HashData(Encoding.UTF8.GetBytes("PureAccessoryERP_IV!"));

    public static string EncryptString(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText)) return "";
        try
        {
            using var aes = Aes.Create();
            aes.Key = AesKey;
            aes.IV = AesIv;
            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText.Trim());
            byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            return "AES:" + Convert.ToBase64String(cipherBytes);
        }
        catch
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText.Trim()));
        }
    }

    public static string DecryptString(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return "";
        try
        {
            if (cipherText.StartsWith("AES:", StringComparison.Ordinal))
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText.Substring(4));
                using var aes = Aes.Create();
                aes.Key = AesKey;
                aes.IV = AesIv;
                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                return Encoding.UTF8.GetString(plainBytes);
            }

            byte[] dpapiBytes = Convert.FromBase64String(cipherText);
            byte[] unprotBytes = ProtectedData.Unprotect(dpapiBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(unprotBytes);
        }
        catch
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cipherText));
                if (decoded.All(c => c >= 32 && c < 127))
                    return decoded;
                return "";
            }
            catch
            {
                return "";
            }
        }
    }
}
