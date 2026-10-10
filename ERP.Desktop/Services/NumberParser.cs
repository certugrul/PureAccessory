using System.Globalization;

namespace ERP.Desktop.Services;

public static class NumberParser
{
    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    /// <summary>
    /// Kullanıcının klavyeden girdiği veya arayüzde düzenlediği parasal tutarları
    /// (Türkçe 25.000,00, 27000, 27.000, 27000,00, 27000.50, vb.) her türlü formatta güvenle parse eder.
    /// </summary>
    public static bool TryParseDecimal(string? input, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string s = input.Replace("₺", "")
                        .Replace("TL", "", StringComparison.OrdinalIgnoreCase)
                        .Trim();

        if (string.IsNullOrWhiteSpace(s)) return false;

        int dotCount = s.Count(c => c == '.');
        int commaCount = s.Count(c => c == ',');

        // 1. Hem nokta hem virgül varsa
        if (dotCount > 0 && commaCount > 0)
        {
            int lastDot = s.LastIndexOf('.');
            int lastComma = s.LastIndexOf(',');

            if (lastComma > lastDot)
            {
                // Türkçe format: 25.000,50 -> noktaları sil, virgülü nokta yap
                s = s.Replace(".", "").Replace(',', '.');
            }
            else
            {
                // US format: 25,000.50 -> virgülleri sil
                s = s.Replace(",", "");
            }
        }
        // 2. Birden fazla virgül varsa (örn: yanlış Replace edilmiş "27,000,00" veya "1,250,000")
        else if (commaCount > 1)
        {
            int lastComma = s.LastIndexOf(',');
            int charsAfterLast = s.Length - lastComma - 1;

            if (charsAfterLast == 2)
            {
                // "27,000,00" -> Son virgül kuruş hanesi, öncekiler binlik ayracı
                string intPart = s.Substring(0, lastComma).Replace(",", "");
                string decPart = s.Substring(lastComma + 1);
                s = intPart + "." + decPart;
            }
            else
            {
                // "1,250,000" -> Hepsi binlik ayracı
                s = s.Replace(",", "");
            }
        }
        // 3. Birden fazla nokta varsa (örn: "1.250.000")
        else if (dotCount > 1)
        {
            s = s.Replace(".", "");
        }
        // 4. Tek bir nokta varsa
        else if (dotCount == 1)
        {
            int dotIndex = s.IndexOf('.');
            int digitsAfter = s.Length - dotIndex - 1;

            // Eğer noktadan sonra tam 3 basamak varsa ve tamsayı kısmı varsa (örn: "25.000", "27.000", "1.500")
            // Bu Türkiye'de binlik ayracıdır!
            if (digitsAfter == 3 && dotIndex > 0)
            {
                s = s.Replace(".", "");
            }
            else
            {
                // Ondalık nokta: "25.50", "25.5" -> Olduğu gibi bırak
            }
        }
        // 5. Tek bir virgül varsa (örn: "25000,00" veya "25000,50" veya "25,00")
        else if (commaCount == 1)
        {
            s = s.Replace(',', '.');
        }

        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Güvenli parse eder, parse edemezse defaultValue (varsayılan 0) döner.
    /// </summary>
    public static decimal ParseDecimalSafe(string? input, decimal defaultValue = 0m)
    {
        return TryParseDecimal(input, out var res) ? res : defaultValue;
    }

    /// <summary>
    /// Bir tutarı düzenleme kutusuna (TextBox) koymak üzere sade formatlar.
    /// Binlik nokta koymaz (böylece kullanıcı 25000'deki 5'i silip 7 yaptığında 27000 kolayca oluşur).
    /// Kuruş varsa virgülle gösterir: "25000" veya "25000,50".
    /// </summary>
    public static string FormatForEditing(decimal amount)
    {
        if (amount % 1 == 0)
        {
            return amount.ToString("0", CultureInfo.InvariantCulture);
        }
        return amount.ToString("0.##", TurkishCulture);
    }
}
