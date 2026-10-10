using System.Diagnostics;
using System.IO;
using ERP.Desktop.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ERP.Desktop.Services;

public class BarcodePrintService
{
    private static readonly string ReportsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
    private readonly BarcodeService _barcodeService;

    public BarcodePrintService(BarcodeService? barcodeService = null)
    {
        _barcodeService = barcodeService ?? BarcodeService.Instance;
        QuestPDF.Settings.License = LicenseType.Community;
        EnsureReportsFolder();
    }

    private static void EnsureReportsFolder()
    {
        try
        {
            if (!Directory.Exists(ReportsFolder))
            {
                Directory.CreateDirectory(ReportsFolder);
            }
        }
        catch { }
    }

    public static string GetDefaultOutputPath(string prefix = "Barkod_Baski")
    {
        EnsureReportsFolder();
        return Path.Combine(ReportsFolder, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
    }

    public static void OpenPdfFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
        }
        catch { }
    }

    public string GenerateBarcodeLabelsPdf(List<LabelPrintItem> items, BarcodePrintOptions options, string? customPath = null)
    {
        var outputPath = customPath ?? GetDefaultOutputPath($"Etiket_{options.PaperFormat}");

        // Yazdırılacak etiketleri düz liste haline getir (Her üründen PrintCount adet)
        var expandedList = new List<Product>();
        foreach (var item in items.Where(i => i.IsSelected && i.PrintCount > 0))
        {
            for (int i = 0; i < item.PrintCount; i++)
            {
                expandedList.Add(item.Product);
            }
        }

        if (expandedList.Count == 0)
        {
            throw new InvalidOperationException("Yazdırılacak ürün bulunamadı. Lütfen en az bir ürün seçin ve adet belirleyin.");
        }

        // Barkod ve QR kod görsellerini önbellekle (aynı ürün için tekrar tekrar üretilmesin)
        var barcodeImageCache = new Dictionary<string, byte[]>();
        var qrImageCache = new Dictionary<string, byte[]>();

        foreach (var p in expandedList)
        {
            string codeToUse = !string.IsNullOrWhiteSpace(p.Barcode) ? p.Barcode : p.Code;
            if (!barcodeImageCache.ContainsKey(codeToUse))
            {
                try
                {
                    barcodeImageCache[codeToUse] = _barcodeService.GenerateBarcodePngBytes(codeToUse, moduleWidth: 2, height: 60);
                }
                catch
                {
                    // Fallback
                    barcodeImageCache[codeToUse] = _barcodeService.GenerateBarcodePngBytes(p.Code, moduleWidth: 2, height: 60);
                }
            }

            if (options.ShowQrCode)
            {
                string qrContent = !string.IsNullOrWhiteSpace(p.QrCode) ? p.QrCode : codeToUse;
                if (!qrImageCache.ContainsKey(qrContent))
                {
                    try
                    {
                        qrImageCache[qrContent] = _barcodeService.GenerateQrCodePngBytes(qrContent, pixelsPerModule: 3);
                    }
                    catch { }
                }
            }
        }

        // Format tipine göre PDF oluştur
        if (options.PaperFormat == LabelPaperFormat.Thermal_60x40 || options.PaperFormat == LabelPaperFormat.Thermal_50x30)
        {
            GenerateThermalPdf(expandedList, options, barcodeImageCache, qrImageCache, outputPath);
        }
        else
        {
            GenerateA4SheetPdf(expandedList, options, barcodeImageCache, qrImageCache, outputPath);
        }

        return outputPath;
    }

    private void GenerateThermalPdf(
        List<Product> products,
        BarcodePrintOptions options,
        Dictionary<string, byte[]> barcodeCache,
        Dictionary<string, byte[]> qrCache,
        string outputPath)
    {
        bool is60x40 = options.PaperFormat == LabelPaperFormat.Thermal_60x40;
        float widthMm = is60x40 ? 60 : 50;
        float heightMm = is60x40 ? 40 : 30;
        float marginMm = is60x40 ? 2 : 1.5f;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(new PageSize(widthMm, heightMm, Unit.Millimetre));
                page.Margin(marginMm, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontColor(Colors.Black));

                page.Content().Column(col =>
                {
                    for (int i = 0; i < products.Count; i++)
                    {
                        var product = products[i];
                        string codeToUse = !string.IsNullOrWhiteSpace(product.Barcode) ? product.Barcode : product.Code;
                        byte[]? barcodeBytes = barcodeCache.GetValueOrDefault(codeToUse);

                        col.Item()
                            .ShowEntire()
                            .Element(c => ComposeThermalLabel(c, product, options, barcodeBytes, is60x40));

                        // Son etiket hariç her etiketten sonra sayfa sonu ekle (rulo kesimi için)
                        if (i < products.Count - 1)
                        {
                            col.Item().PageBreak();
                        }
                    }
                });
            });
        }).GeneratePdf(outputPath);
    }

    private static void ComposeThermalLabel(IContainer container, Product product, BarcodePrintOptions options, byte[]? barcodeBytes, bool is60x40)
    {
        container.Column(col =>
        {
            col.Spacing(0.5f);

            // 1. Mağaza Başlığı
            if (options.ShowStoreName && !string.IsNullOrWhiteSpace(options.StoreName))
            {
                col.Item().AlignCenter().Text(options.StoreName)
                    .FontSize(is60x40 ? 6.5f : 5.5f)
                    .Bold()
                    .FontColor(Colors.Grey.Darken3);
            }

            // 2. Ürün Adı
            if (options.ShowProductName)
            {
                col.Item().AlignCenter().Text(product.Name)
                    .FontSize(is60x40 ? 7.5f : 6.5f)
                    .Bold()
                    .ClampLines(1);
            }

            // 3. Barkod Görseli
            if (barcodeBytes != null && barcodeBytes.Length > 0)
            {
                col.Item().AlignCenter().Height(is60x40 ? 12 : 9, Unit.Millimetre).Image(barcodeBytes);
            }

            // 4. Barkod Metni
            if (options.ShowBarcodeText)
            {
                string displayCode = !string.IsNullOrWhiteSpace(product.Barcode) ? product.Barcode : product.Code;
                col.Item().AlignCenter().Text(displayCode)
                    .FontSize(is60x40 ? 7 : 6)
                    .SemiBold();
            }

            // 5. Fiyat
            if (options.ShowPrice)
            {
                col.Item().AlignCenter().Text($"₺{product.SalePrice:N2}")
                    .FontSize(is60x40 ? 10 : 8.5f)
                    .ExtraBold();
            }
        });
    }

    private void GenerateA4SheetPdf(
        List<Product> products,
        BarcodePrintOptions options,
        Dictionary<string, byte[]> barcodeCache,
        Dictionary<string, byte[]> qrCache,
        string outputPath)
    {
        int cols = 3;
        float cardHeightMm = 33f;

        if (options.PaperFormat == LabelPaperFormat.A4_30Labels)
        {
            cols = 3;
            cardHeightMm = 26.5f;
        }
        else if (options.PaperFormat == LabelPaperFormat.A4_40Labels)
        {
            cols = 4;
            cardHeightMm = 26.5f;
        }

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(6, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontColor(Colors.Black));

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        for (int c = 0; c < cols; c++)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    foreach (var product in products)
                    {
                        string codeToUse = !string.IsNullOrWhiteSpace(product.Barcode) ? product.Barcode : product.Code;
                        byte[]? barcodeBytes = barcodeCache.GetValueOrDefault(codeToUse);

                        table.Cell()
                            .Padding(1.5f, Unit.Millimetre)
                            .ShowEntire()
                            .Element(c => ComposeA4LabelCard(c, product, options, barcodeBytes, cardHeightMm));
                    }
                });
            });
        }).GeneratePdf(outputPath);
    }

    private static void ComposeA4LabelCard(
        IContainer container,
        Product product,
        BarcodePrintOptions options,
        byte[]? barcodeBytes,
        float cardHeightMm)
    {
        container
            .Height(cardHeightMm, Unit.Millimetre)
            .Border(0.5f)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(2, Unit.Millimetre)
            .Column(col =>
            {
                col.Spacing(1);

                // Üst Başlık (Mağaza Adı)
                if (options.ShowStoreName && !string.IsNullOrWhiteSpace(options.StoreName))
                {
                    col.Item().AlignCenter().Text(options.StoreName)
                        .FontSize(6.5f)
                        .Bold()
                        .FontColor(Colors.Grey.Darken2);
                }

                // Ürün Adı
                if (options.ShowProductName)
                {
                    col.Item().AlignCenter().Text(product.Name)
                        .FontSize(7.5f)
                        .Bold()
                        .ClampLines(1);
                }

                // 1D Barkod Çizgileri
                if (barcodeBytes != null && barcodeBytes.Length > 0)
                {
                    col.Item().AlignCenter().Height(10.5f, Unit.Millimetre).Image(barcodeBytes);
                }

                // Barkod Numarası
                if (options.ShowBarcodeText)
                {
                    string displayCode = !string.IsNullOrWhiteSpace(product.Barcode) ? product.Barcode : product.Code;
                    col.Item().AlignCenter().Text(displayCode)
                        .FontSize(6.5f)
                        .SemiBold();
                }

                // Satış Fiyatı
                if (options.ShowPrice)
                {
                    col.Item().AlignCenter().Text($"₺{product.SalePrice:N2}")
                        .FontSize(9.5f)
                        .ExtraBold();
                }
            });
    }
}
