using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ERP.Desktop.Models;
using QRCoder;

namespace ERP.Desktop.Services;

public class BarcodeService
{
    private static readonly Lazy<BarcodeService> _instance = new(() => new BarcodeService());
    public static BarcodeService Instance => _instance.Value;

    // EAN-13 L-Codes (Odd parity)
    private static readonly string[] EanL = [
        "0001101", "0011001", "0010011", "0111101", "0100011",
        "0110001", "0101111", "0111011", "0110111", "0001011"
    ];

    // EAN-13 G-Codes (Even parity)
    private static readonly string[] EanG = [
        "0100111", "0110011", "0011011", "0100001", "0011101",
        "0111001", "0000101", "0010001", "0001001", "0010111"
    ];

    // EAN-13 R-Codes (Right side)
    private static readonly string[] EanR = [
        "1110010", "1100110", "1101100", "1000010", "1011100",
        "1001110", "1010000", "1000100", "1001000", "1110100"
    ];

    // EAN-13 First Digit Parity structure for digits 2..7 (L=0, G=1)
    private static readonly string[] EanParity = [
        "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG",
        "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"
    ];

    // Code 128 Patterns (values 0 to 106)
    private static readonly string[] Code128Patterns = [
        "11011001100", "11001101100", "11001100110", "10010011000", "10010001100",
        "10001001100", "10011001000", "10011000100", "10001100100", "11001001000",
        "11001000100", "11000100100", "10110011100", "10011011100", "10011001110",
        "10111001100", "10011101100", "10011100110", "11001110010", "11001011100",
        "11001001110", "11011100100", "11001110100", "11101101110", "11101001100",
        "11100101100", "11100100110", "11101100100", "11100110100", "11100110010",
        "11011011000", "11011000110", "11000110110", "10100011000", "10001011000",
        "10001000110", "10110001000", "10001101000", "10001100010", "11010001000",
        "11000101000", "11000100010", "10110111000", "10110001110", "10001101110",
        "10111011000", "10111000110", "10001110110", "11101110110", "11010001110",
        "11000101110", "11011101000", "11011100010", "11011101110", "11101011000",
        "11101000110", "11100010110", "11101101000", "11101100010", "11100011010",
        "11101111010", "11001000010", "11110001010", "10100110000", "10100001100",
        "10010110000", "10010000110", "10000101100", "10000100110", "10110010000",
        "10110000100", "10011010000", "10011000010", "10000110100", "10000110010",
        "11000010010", "11001010000", "11110111010", "11000010100", "10001111010",
        "10100111100", "10010111100", "10010011110", "10111100100", "10011110100",
        "10011110010", "11110100100", "11110010100", "11110010010", "11011011110",
        "11011110110", "11110110110", "10101111000", "10100011110", "10001011110",
        "10111101000", "10111100010", "11110101000", "11110100010", "10111011110",
        "10111101110", "11101011110", "11110101110", "11010000100", "11010010000",
        "11010011100", "1100011101011" // 106: STOP
    ];

    public BarcodeService() { }

    /// <summary>
    /// EAN-13 için verilen 12 hanenin 13. kontrol hanesini hesaplar (Modulo-10).
    /// </summary>
    public static int CalculateEan13CheckDigit(string twelveDigits)
    {
        if (twelveDigits.Length < 12)
        {
            twelveDigits = twelveDigits.PadRight(12, '0');
        }
        else if (twelveDigits.Length > 12)
        {
            twelveDigits = twelveDigits[..12];
        }

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int digit = twelveDigits[i] - '0';
            sum += (i % 2 == 0) ? digit * 1 : digit * 3;
        }

        int remainder = sum % 10;
        return (10 - remainder) % 10;
    }

    /// <summary>
    /// 13 haneli EAN barkodunun matematiksel olarak geçerli olup olmadığını kontrol eder.
    /// </summary>
    public static bool IsValidEan13(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode) || barcode.Length != 13 || !barcode.All(char.IsDigit))
        {
            return false;
        }

        int expectedCheck = CalculateEan13CheckDigit(barcode[..12]);
        int actualCheck = barcode[12] - '0';
        return expectedCheck == actualCheck;
    }

    /// <summary>
    /// Türkiye standartlarında (869... veya 868...) geçerli ve benzersiz 13 haneli EAN-13 barkod üretir.
    /// </summary>
    public string GenerateEan13Barcode(IEnumerable<string>? existingBarcodes = null, string prefix = "869")
    {
        var existingSet = new HashSet<string>(existingBarcodes ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        for (int attempt = 0; attempt < 500; attempt++)
        {
            // 869 + 9 rastgele rakam + 1 checksum
            string random9 = RandomNumberGenerator.GetInt32(100_000_000, 999_999_999).ToString();
            string twelve = $"{prefix}{random9}";
            int check = CalculateEan13CheckDigit(twelve);
            string fullBarcode = $"{twelve}{check}";

            if (!existingSet.Contains(fullBarcode))
            {
                return fullBarcode;
            }
        }

        return $"{prefix}{DateTime.Now:MMddHHmm}{CalculateEan13CheckDigit(prefix + DateTime.Now.ToString("MMddHHmm"))}";
    }

    /// <summary>
    /// Perakende mağaza içi serbest kullanım için 200... ile başlayan 13 haneli EAN-13 barkod üretir.
    /// </summary>
    public string GenerateStoreEan13Barcode(IEnumerable<string>? existingBarcodes = null)
    {
        return GenerateEan13Barcode(existingBarcodes, "200");
    }

    /// <summary>
    /// Harf ve rakam içeren alfanümerik Code-128 formatında benzersiz barkod üretir (örn: PRD869001).
    /// </summary>
    public string GenerateCode128Barcode(IEnumerable<string>? existingBarcodes = null, string prefix = "PRD")
    {
        var existingSet = new HashSet<string>(existingBarcodes ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        for (int attempt = 0; attempt < 500; attempt++)
        {
            string number = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            string code = $"{prefix.ToUpper()}-{number}";
            if (!existingSet.Contains(code))
            {
                return code;
            }
        }

        return $"{prefix.ToUpper()}-{DateTime.Now:yyMMddHHmm}";
    }

    /// <summary>
    /// Seçilen barkod türüne göre benzersiz yeni bir barkod metni üretir.
    /// </summary>
    public string GenerateBarcode(BarcodeType type, IEnumerable<string>? existingBarcodes = null, string customPrefix = "869")
    {
        return type switch
        {
            BarcodeType.Ean13 => GenerateEan13Barcode(existingBarcodes, "869"),
            BarcodeType.StoreEan13 => GenerateStoreEan13Barcode(existingBarcodes),
            BarcodeType.Code128 => GenerateCode128Barcode(existingBarcodes, "PRD"),
            BarcodeType.Custom => customPrefix.All(char.IsDigit) && customPrefix.Length <= 11 
                                    ? GenerateEan13Barcode(existingBarcodes, customPrefix) 
                                    : GenerateCode128Barcode(existingBarcodes, customPrefix),
            _ => GenerateEan13Barcode(existingBarcodes, "869")
        };
    }

    /// <summary>
    /// Barkod metnini 1D modül dizisine (true = siyah çizgi, false = beyaz boşluk) dönüştürür.
    /// </summary>
    public bool[] EncodeToBarcodeModules(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            code = "0000000000000";
        }
        else
        {
            code = code.Trim();
        }

        // 13 haneli numerik ise EAN-13 olarak kodla
        if (code.Length == 13 && code.All(char.IsDigit))
        {
            return EncodeEan13Modules(code);
        }

        // 12 haneli numerik ise checksum ekleyip EAN-13 yap
        if (code.Length == 12 && code.All(char.IsDigit))
        {
            string ean = $"{code}{CalculateEan13CheckDigit(code)}";
            return EncodeEan13Modules(ean);
        }

        // Diğer tüm durumlar için Code-128B kodla
        return EncodeCode128BModules(code);
    }

    private static bool[] EncodeEan13Modules(string ean13)
    {
        var modules = new List<bool>(95);

        int firstDigit = ean13[0] - '0';
        string parity = EanParity[firstDigit];

        // Start guard: 101
        modules.AddRange([true, false, true]);

        // Left 6 digits (d2 to d7)
        for (int i = 0; i < 6; i++)
        {
            int digit = ean13[i + 1] - '0';
            char p = parity[i];
            string pattern = (p == 'L') ? EanL[digit] : EanG[digit];
            foreach (char c in pattern)
            {
                modules.Add(c == '1');
            }
        }

        // Center guard: 01010
        modules.AddRange([false, true, false, true, false]);

        // Right 6 digits (d8 to d13)
        for (int i = 7; i < 13; i++)
        {
            int digit = ean13[i] - '0';
            string pattern = EanR[digit];
            foreach (char c in pattern)
            {
                modules.Add(c == '1');
            }
        }

        // End guard: 101
        modules.AddRange([true, false, true]);

        return modules.ToArray();
    }

    private static bool[] EncodeCode128BModules(string text)
    {
        var modules = new List<bool>();

        // Start B (index 104)
        int startVal = 104;
        AppendPattern(modules, Code128Patterns[startVal]);

        int checksum = startVal;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            int val = ch - 32;
            if (val < 0 || val > 95) val = 0; // Güvenli sınır

            AppendPattern(modules, Code128Patterns[val]);
            checksum += val * (i + 1);
        }

        int checkVal = checksum % 103;
        AppendPattern(modules, Code128Patterns[checkVal]);

        // Stop (index 106)
        AppendPattern(modules, Code128Patterns[106]);

        return modules.ToArray();
    }

    private static void AppendPattern(List<bool> list, string pattern)
    {
        foreach (char c in pattern)
        {
            list.Add(c == '1');
        }
    }

    /// <summary>
    /// Barkod modüllerinden yüksek kaliteli PNG görsel byte dizisi üretir.
    /// </summary>
    public byte[] GenerateBarcodePngBytes(string? code, int moduleWidth = 2, int height = 70)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            code = "0000000000000";
        }

        bool[] modules = EncodeToBarcodeModules(code);

        int quietZoneModules = 10;
        int totalModules = modules.Length + (quietZoneModules * 2);
        int width = totalModules * moduleWidth;

        // Bgra32 bitmap tamponu
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        // Tamamını beyaz yap (255, 255, 255, 255)
        Array.Fill(pixels, (byte)255);

        // Çizgileri siyah olarak çiz
        for (int m = 0; m < modules.Length; m++)
        {
            if (modules[m]) // Siyah çizgi
            {
                int startX = (quietZoneModules + m) * moduleWidth;
                for (int w = 0; w < moduleWidth; w++)
                {
                    int x = startX + w;
                    for (int y = 0; y < height; y++)
                    {
                        int index = (y * stride) + (x * 4);
                        pixels[index] = 0;     // Blue
                        pixels[index + 1] = 0; // Green
                        pixels[index + 2] = 0; // Red
                        pixels[index + 3] = 255;// Alpha
                    }
                }
            }
        }

        var bitmapSource = BitmapSource.Create(
            width, height, 96, 96,
            PixelFormats.Bgra32, null,
            pixels, stride);

        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// WPF Arayüzünde `<Image Source="..."/>` olarak gösterilmek üzere BitmapSource üretir.
    /// </summary>
    public BitmapSource GenerateBarcodeBitmapSource(string? code, int moduleWidth = 2, int height = 70)
    {
        byte[] bytes = GenerateBarcodePngBytes(code, moduleWidth, height);
        using var ms = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// QRCoder ile QR kod PNG görsel byte dizisi üretir.
    /// </summary>
    public byte[] GenerateQrCodePngBytes(string content, int pixelsPerModule = 4)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        return qrCode.GetGraphic(pixelsPerModule);
    }
}
