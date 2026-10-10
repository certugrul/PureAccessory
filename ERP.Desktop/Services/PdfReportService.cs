using System.Diagnostics;
using System.IO;
using ERP.Desktop.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ERP.Desktop.Services;

public class PdfReportService
{
    private static readonly string ReportsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");

    public PdfReportService()
    {
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

    public static string GetDefaultOutputPath(string reportPrefix)
    {
        EnsureReportsFolder();
        var fileName = $"{reportPrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        return Path.Combine(ReportsFolder, fileName);
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

    // 1. Z RAPORU (GÜN SONU MALİ KAPANIŞ RAPORU)
    public string GenerateZReportPdf(ZReportData data, string? customPath = null)
    {
        var outputPath = customPath ?? GetDefaultOutputPath("Z_Raporu");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(35);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeHeader(c, "Z RAPORU (GÜN SONU MALİ KAPANIŞ)", $"Rapor No: Z-{data.ClosureId:D6} | Tarih: {data.ClosureDate:dd.MM.yyyy}"));

                page.Content().PaddingVertical(15).Column(col =>
                {
                    col.Spacing(12);

                    // Bilgi Kartları
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                        {
                            c.Item().Text("Kapanış Saati").FontSize(9).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"{data.ClosedAt:HH:mm:ss}").FontSize(14).Bold().FontColor(Colors.Blue.Medium);
                        });
                        row.ConstantItem(10);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                        {
                            c.Item().Text("Toplam Fiş / Satış").FontSize(9).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"{data.SalesCount} Adet").FontSize(14).Bold().FontColor(Colors.Grey.Darken4);
                        });
                        row.ConstantItem(10);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                        {
                            c.Item().Text("Toplam Günlük Ciro").FontSize(9).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.TotalRevenue:N2}").FontSize(15).Bold().FontColor(Colors.Green.Darken2);
                        });
                    });

                    // Tahsilat Dağılımı Tablosu
                    col.Item().Text("TAHSİLAT VE GELİR DAĞILIMI").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text("Ödeme Türü").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Tutar (₺)").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Ciro Payı").Bold();
                        });

                        decimal rev = data.TotalRevenue > 0 ? data.TotalRevenue : 1;

                        AddTableRow(table, "Nakit Tahsilat", $"₺{data.TotalCash:N2}", $"%{(data.TotalCash / rev * 100):N1}");
                        AddTableRow(table, "Kredi Kartı / Banka Kartı", $"₺{data.TotalCreditCard:N2}", $"%{(data.TotalCreditCard / rev * 100):N1}");
                        AddTableRow(table, "Veresiye / Açık Hesap", $"₺{data.TotalOnAccount:N2}", $"%{(data.TotalOnAccount / rev * 100):N1}");

                        table.Cell().ColumnSpan(3).BorderTop(1).BorderColor(Colors.Grey.Medium);
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("TOPLAM CİRO").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.TotalRevenue:N2}").Bold().FontColor(Colors.Green.Darken2);
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text("%100,0").Bold();
                    });

                    // Maliyet ve Kârlılık Özeti
                    col.Item().PaddingTop(10).Text("MALİYET, GİDER VE KÂRLILIK DÖKÜMÜ").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(3);
                        });

                        AddTableRow(table, "Satılan Malların Toplam Maliyeti", $"₺{data.TotalCost:N2}");
                        AddTableRow(table, "Kaydedilen İşletme Giderleri", $"₺{data.TotalExpenses:N2}");
                        AddTableRow(table, "Kasa Devir / Başlangıç Nakiti", $"₺{data.OpeningCash:N2}");

                        table.Cell().ColumnSpan(2).BorderTop(1.5f).BorderColor(Colors.Blue.Medium);
                        table.Cell().Background(Colors.Blue.Lighten5).Padding(8).Text("NET GÜNLÜK KÂR").Bold().FontSize(12).FontColor(Colors.Blue.Darken3);
                        table.Cell().Background(Colors.Blue.Lighten5).Padding(8).AlignRight().Text($"₺{data.TotalProfit:N2}").Bold().FontSize(13).FontColor(Colors.Blue.Darken3);
                    });

                    // Not Alanı
                    if (!string.IsNullOrWhiteSpace(data.Notes))
                    {
                        col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                        {
                            c.Item().Text("Kapanış Notları:").Bold().FontSize(10);
                            c.Item().Text(data.Notes).FontSize(9);
                        });
                    }
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    // 2. X RAPORU (GÜN ORTASI ARA DURUM RAPORU)
    public string GenerateXReportPdf(XReportData data, string? customPath = null)
    {
        var outputPath = customPath ?? GetDefaultOutputPath("X_Raporu");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(35);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeHeader(c, "X RAPORU (GÜN ORTASI ARA DURUM)", $"Tarih: {data.TargetDate:dd.MM.yyyy} | Rapor Saati: {data.ReportTime:HH:mm:ss} (Mali Değeri Yoktur)"));

                page.Content().PaddingVertical(15).Column(col =>
                {
                    col.Spacing(12);

                    // Kasa Durum Kartı
                    col.Item().Background(data.IsCashRegisterOpen ? Colors.Green.Lighten5 : Colors.Red.Lighten5)
                             .Border(1)
                             .BorderColor(data.IsCashRegisterOpen ? Colors.Green.Medium : Colors.Red.Medium)
                             .Padding(12)
                             .Row(row =>
                             {
                                 row.RelativeItem().Column(c =>
                                 {
                                     c.Item().Text(data.IsCashRegisterOpen ? "🟢 KASA AÇIK DURUMDA" : "🔴 KASA KAPALI")
                                             .Bold().FontSize(12)
                                             .FontColor(data.IsCashRegisterOpen ? Colors.Green.Darken3 : Colors.Red.Darken3);
                                     c.Item().Text($"Açılış Saati: {(data.CashOpenedAt.HasValue ? data.CashOpenedAt.Value.ToString("HH:mm:ss") : "-")} | Başlangıç Nakiti: ₺{data.OpeningCash:N2}").FontSize(10);
                                 });
                                 row.ConstantItem(150).AlignRight().Column(c =>
                                 {
                                     c.Item().Text("Kasada Bulunan Nakit").FontSize(9).FontColor(Colors.Grey.Medium);
                                     c.Item().Text($"₺{data.CurrentCashBalance:N2}").Bold().FontSize(14).FontColor(Colors.Blue.Darken2);
                                 });
                             });

                    // Anlık Satış ve Gelir Tablosu
                    col.Item().Text("GÜN İÇİ ANLIK SATIŞLAR (SON KASADAN İTİBAREN)").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text("Açıklama").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Tutar (₺)").Bold();
                        });

                        AddTableRow(table, $"Toplam Tamamlanan Satış ({data.SalesCount} Fiş)", $"₺{data.TotalRevenue:N2}");
                        AddTableRow(table, "• Nakit Satışlar Toplamı", $"₺{data.CashSales:N2}");
                        AddTableRow(table, "• Kredi Kartı Satışları", $"₺{data.CreditCardSales:N2}");
                        AddTableRow(table, "• Veresiye Satışlar", $"₺{data.OnAccountSales:N2}");
                        AddTableRow(table, "Manuel Kasaya Para Girişi", $"+₺{data.ManualCashIn:N2}");
                        AddTableRow(table, "Manuel Kasadan Para Çıkışı / Masraf", $"-₺{data.ManualCashOut:N2}");

                        table.Cell().ColumnSpan(2).BorderTop(1).BorderColor(Colors.Grey.Medium);
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("FİZİKSEL OLMASI GEREKEN NAKİT").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.CurrentCashBalance:N2}").Bold().FontSize(12).FontColor(Colors.Green.Darken2);
                    });

                    // Ara Kârlılık Durumu
                    col.Item().PaddingTop(8).Text("ANLIK TAHMİNİ KÂR DURUMU").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                        });

                        AddTableRow(table, "Satılan Malların Anlık Maliyeti", $"₺{data.TotalCost:N2}");
                        AddTableRow(table, "Gün Ortası Tahmini Net Kâr", $"₺{data.EstimatedProfit:N2}");
                    });
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    // 3. DETAYLI SATIŞ RAPORU
    public string GenerateSalesReportPdf(SalesReportData data, string? customPath = null)
    {
        var outputPath = customPath ?? GetDefaultOutputPath("Satis_Raporu");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken3));

                string sub = data.StartDate == data.EndDate 
                    ? $"Tarih: {data.StartDate:dd.MM.yyyy}" 
                    : $"Dönem: {data.StartDate:dd.MM.yyyy} - {data.EndDate:dd.MM.yyyy}";

                page.Header().Element(c => ComposeHeader(c, "DETAYLI SATIŞ RAPORU", sub));

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(10);

                    // Özet Kartlar
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Toplam Fiş").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"{data.TotalSalesCount} Adet").FontSize(12).Bold();
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Nakit Ciro").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.TotalCash:N2}").FontSize(12).Bold().FontColor(Colors.Blue.Medium);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Kartlı Ciro").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.TotalCreditCard:N2}").FontSize(12).Bold().FontColor(Colors.Blue.Medium);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Toplam Ciro").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.TotalRevenue:N2}").FontSize(13).Bold().FontColor(Colors.Green.Darken2);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Toplam Kâr").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.TotalProfit:N2}").FontSize(13).Bold().FontColor(Colors.Green.Darken3);
                        });
                    });

                    // Satış Tablosu
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(50); // Fiş No
                            columns.ConstantColumn(85); // Tarih/Saat
                            columns.ConstantColumn(75); // Ödeme Tipi
                            columns.ConstantColumn(45); // Kalem
                            columns.RelativeColumn(2);  // Ara Toplam
                            columns.RelativeColumn(2);  // KDV
                            columns.RelativeColumn(2);  // Genel Toplam
                            columns.RelativeColumn(2);  // Kâr
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Fiş #").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Tarih/Saat").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Ödeme").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Ürün").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Ara Tutar").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("KDV").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Genel Toplam").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Net Kâr").Bold();
                        });

                        foreach (var item in data.Items)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text($"#{item.SaleId}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text($"{item.SaleDate:dd.MM HH:mm}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(item.PaymentType);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text($"{item.ItemCount}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{item.SubTotal:N2}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{item.VatTotal:N2}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{item.GrandTotal:N2}").Bold();
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{item.Profit:N2}").FontColor(Colors.Green.Darken2);
                        }
                    });
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    // 4. AYLIK SATIŞ RAPORU (CİRO BAZLI)
    public string GenerateMonthlySalesReportPdf(MonthlySalesReportData data, string? customPath = null)
    {
        var outputPath = customPath ?? GetDefaultOutputPath($"Aylik_Satis_{data.Year}_{data.Month:D2}");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeHeader(c, "AYLIK SATIŞ RAPORU (GÜNLÜK CİRO ANALİZİ)", $"Dönem: {data.MonthName.ToUpper()}"));

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(10);

                    // Aylık İstatistik Kartları
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Aylık Toplam Ciro").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.GrandTotal:N2}").FontSize(14).Bold().FontColor(Colors.Green.Darken2);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Toplam Fiş Sayısı").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"{data.TotalSalesCount} Fiş").FontSize(13).Bold();
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Günlük Ortalama Ciro").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.DailyAverage:N2}").FontSize(13).Bold().FontColor(Colors.Blue.Medium);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("En Yüksek Cirolu Gün").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text(data.BestDay.HasValue ? $"{data.BestDay.Value:dd.MM} (₺{data.BestDayTotal:N0})" : "-").FontSize(11).Bold();
                        });
                    });

                    // Günlük Tablo
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(80); // Tarih
                            columns.ConstantColumn(60); // Fiş Adedi
                            columns.RelativeColumn(2);  // Nakit
                            columns.RelativeColumn(2);  // Kart
                            columns.RelativeColumn(2);  // Veresiye
                            columns.RelativeColumn(3);  // Günlük Ciro
                            columns.RelativeColumn(3);  // Kümülatif Ciro
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Tarih").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Fiş #").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Nakit").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Kredi Kartı").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Veresiye").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Günlük Toplam").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Kümülatif").Bold();
                        });

                        foreach (var day in data.Days)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text($"{day.Date:dd.MM ddd}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(day.SalesCount > 0 ? $"{day.SalesCount}" : "-");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{day.CashTotal:N2}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{day.CardTotal:N2}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{day.OnAccountTotal:N2}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{day.DailyTotal:N2}").Bold();
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{day.CumulativeTotal:N2}").FontColor(Colors.Blue.Medium);
                        }

                        // Toplam Satırı
                        table.Cell().ColumnSpan(2).Background(Colors.Grey.Lighten4).Padding(6).Text("GENEL TOPLAM").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.TotalCash:N2}").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.TotalCreditCard:N2}").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.TotalOnAccount:N2}").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.GrandTotal:N2}").Bold().FontColor(Colors.Green.Darken2);
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.GrandTotal:N2}").Bold();
                    });
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    // YILLIK CİRO RAPORU
    public string GenerateYearlySalesReportPdf(YearlySalesReportData data, string? customPath = null)
    {
        var outputPath = customPath ?? GetDefaultOutputPath($"Yillik_Ciro_{data.Year}");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeHeader(c, "YILLIK CİRO VE SATIŞ RAPORU", $"Yıl: {data.Year}"));

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(10);

                    // Özet Kutucukları
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Yıllık Toplam Ciro").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.GrandTotal:N2}").FontSize(14).Bold().FontColor(Colors.Green.Darken2);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Aylık Ortalama Ciro").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.MonthlyAverage:N2}").FontSize(14).Bold().FontColor(Colors.Blue.Medium);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("En Yüksek Ay").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text(data.BestMonthName).FontSize(13).Bold().FontColor(Colors.Grey.Darken4);
                            c.Item().Text($"₺{data.BestMonthTotal:N2}").FontSize(10).FontColor(Colors.Grey.Medium);
                        });
                    });

                    // Tablo
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2); // Ay
                            columns.RelativeColumn(1.2f); // Fiş
                            columns.RelativeColumn(2); // Nakit
                            columns.RelativeColumn(2); // Kart
                            columns.RelativeColumn(1.8f); // Veresiye
                            columns.RelativeColumn(2.5f); // Aylık Ciro
                            columns.RelativeColumn(2.5f); // Kümülatif
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Ay").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Fiş").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Nakit").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Kredi Kartı").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Veresiye").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Aylık Ciro").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Kümülatif").Bold();
                        });

                        foreach (var m in data.Months)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(m.MonthName);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(m.SalesCount > 0 ? $"{m.SalesCount}" : "-");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{m.CashTotal:N2}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{m.CardTotal:N2}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{m.OnAccountTotal:N2}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{m.MonthlyTotal:N2}").Bold();
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{m.CumulativeTotal:N2}");
                        }

                        // Genel Toplam
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("GENEL TOPLAM").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignCenter().Text($"{data.TotalSalesCount}").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.TotalCash:N2}").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.TotalCreditCard:N2}").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.TotalOnAccount:N2}").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.GrandTotal:N2}").Bold().FontColor(Colors.Green.Darken2);
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.GrandTotal:N2}").Bold();
                    });
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    // 5. AYLIK ÜRÜN SATIŞ RAPORU
    public string GenerateMonthlyProductReportPdf(MonthlyProductReportData data, string? customPath = null)
    {
        var outputPath = customPath ?? GetDefaultOutputPath($"Aylik_Urun_{data.Year}_{data.Month:D2}");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeHeader(c, "AYLIK ÜRÜN SATIŞ VE PERFORMANS RAPORU", $"Dönem: {data.MonthName.ToUpper()}"));

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(10);

                    // Özet Başlıklar
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Satılan Toplam Ürün Adedi").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"{data.TotalQuantitySold:N0} Adet").FontSize(13).Bold().FontColor(Colors.Blue.Medium);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Ürün Satış Cirosu").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.TotalRevenue:N2}").FontSize(14).Bold().FontColor(Colors.Green.Darken2);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Elde Edilen Toplam Kâr").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.TotalProfit:N2}").FontSize(14).Bold().FontColor(Colors.Green.Darken3);
                        });
                    });

                    // Ürün Satış Tablosu
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(30);  // Sıra
                            columns.ConstantColumn(80);  // Kod
                            columns.RelativeColumn(4);   // Ürün Adı
                            columns.ConstantColumn(50);  // Adet
                            columns.RelativeColumn(2);   // Ort. Fiyat
                            columns.RelativeColumn(2.5f);// Toplam Ciro
                            columns.RelativeColumn(2.5f);// Toplam Kâr
                            columns.ConstantColumn(45);  // Pay %
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("#").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Kod").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Ürün Adı").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Adet").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Birim").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Ciro").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Kâr").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Pay").Bold();
                        });

                        foreach (var item in data.Products)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text($"{item.Rank}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(item.ProductCode);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(item.ProductName).Bold();
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text($"{item.TotalQuantity}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{item.AverageUnitPrice:N2}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{item.TotalRevenue:N2}").Bold();
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"₺{item.Profit:N2}").FontColor(Colors.Green.Darken2);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text($"%{item.RevenueSharePercent:N1}");
                        }
                    });
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    // 6. KÂR / ZARAR RAPORU (GELİR - GİDER TABLOSU)
    public string GenerateProfitLossReportPdf(ProfitLossReportData data, string? customPath = null)
    {
        var outputPath = customPath ?? GetDefaultOutputPath("Kar_Zarar_Raporu");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(35);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeHeader(c, "KÂR / ZARAR (GELİR - GİDER) RAPORU", $"Dönem: {data.PeriodTitle}"));

                page.Content().PaddingVertical(15).Column(col =>
                {
                    col.Spacing(14);

                    // Büyük Net Kâr Kutusu
                    bool isProfit = data.NetProfitOrLoss >= 0;
                    col.Item().Background(isProfit ? Colors.Green.Lighten5 : Colors.Red.Lighten5)
                             .Border(1.5f)
                             .BorderColor(isProfit ? Colors.Green.Medium : Colors.Red.Medium)
                             .Padding(14)
                             .Row(row =>
                             {
                                 row.RelativeItem().Column(c =>
                                 {
                                     c.Item().Text(isProfit ? "DÖNEM NET KÂRI" : "DÖNEM NET ZARARI")
                                             .Bold().FontSize(12)
                                             .FontColor(isProfit ? Colors.Green.Darken3 : Colors.Red.Darken3);
                                     c.Item().Text($"Dönem kârlılık marjı: %{data.ProfitMarginPercent:N2}").FontSize(10);
                                 });
                                 row.ConstantItem(180).AlignRight().Column(c =>
                                 {
                                     c.Item().Text($"₺{Math.Abs(data.NetProfitOrLoss):N2}")
                                             .Bold().FontSize(18)
                                             .FontColor(isProfit ? Colors.Green.Darken3 : Colors.Red.Darken3);
                                 });
                             });

                    // Gelir Tablosu Bölümü
                    col.Item().Text("1. GELİRLER (SATIŞLAR)").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                        });

                        AddTableRow(table, "Brüt Satış Gelirleri (KDV Dahil Ciro)", $"₺{data.GrossRevenue:N2}");
                        AddTableRow(table, "Hesaplanan KDV Toplamı", $"-₺{data.VatTotal:N2}");

                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("NET SATIŞ GELİRİ").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignRight().Text($"₺{data.NetSalesRevenue:N2}").Bold();
                    });

                    // Maliyet ve Brüt Kâr
                    col.Item().PaddingTop(5).Text("2. SATIŞLARIN MALİYETİ & BRÜT KÂR").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                        });

                        AddTableRow(table, "Satılan Malların Maliyeti (STMM)", $"-₺{data.CostOfGoodsSold:N2}");

                        table.Cell().Background(Colors.Blue.Lighten5).Padding(6).Text("BRÜT FAALİYET KÂRI").Bold().FontColor(Colors.Blue.Darken3);
                        table.Cell().Background(Colors.Blue.Lighten5).Padding(6).AlignRight().Text($"₺{data.GrossProfit:N2}").Bold().FontColor(Colors.Blue.Darken3);
                    });

                    // Faaliyet Giderleri ve Net Kâr
                    col.Item().PaddingTop(5).Text("3. FAALİYET GİDERLERİ & NET KÂR").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                        });

                        AddTableRow(table, "Genel Faaliyet Giderleri & Kasa Masrafları", $"-₺{data.TotalOperatingExpenses:N2}");

                        table.Cell().ColumnSpan(2).BorderTop(1.5f).BorderColor(isProfit ? Colors.Green.Medium : Colors.Red.Medium);
                        table.Cell().Background(isProfit ? Colors.Green.Lighten5 : Colors.Red.Lighten5).Padding(8).Text("NET DÖNEM FAALİYET SONUCU").Bold().FontSize(11).FontColor(isProfit ? Colors.Green.Darken3 : Colors.Red.Darken3);
                        table.Cell().Background(isProfit ? Colors.Green.Lighten5 : Colors.Red.Lighten5).Padding(8).AlignRight().Text($"₺{data.NetProfitOrLoss:N2}").Bold().FontSize(13).FontColor(isProfit ? Colors.Green.Darken3 : Colors.Red.Darken3);
                    });
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    // 7. GÜNLÜK FİŞ VE SATILAN ÜRÜNLER DÖKÜMÜ RAPORU
    public string GenerateDailyReceiptBreakdownPdf(DailyReceiptBreakdownData data, string? customPath = null)
    {
        var outputPath = customPath ?? GetDefaultOutputPath($"Gunun_Fis_Dokumu_{data.TargetDate:yyyyMMdd}");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeHeader(c, "GÜNLÜK FİŞ VE SATILAN ÜRÜNLER DÖKÜMÜ", $"İşlem Tarihi: {data.TargetDate:dd.MM.yyyy} | {data.TotalSalesCount} Fiş | Toplam {data.TotalQuantitySold:N0} Adet Ürün"));

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(10);

                    // 1. ÖZET GÖSTERGELER
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Toplam Fiş Sayısı").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"{data.TotalSalesCount} Fiş").FontSize(13).Bold().FontColor(Colors.Blue.Medium);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Toplam Satılan Adet").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"{data.TotalQuantitySold:N0} Adet").FontSize(13).Bold().FontColor(Colors.Green.Darken2);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Ürün Çeşidi").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"{data.TotalDistinctProducts} Çeşit").FontSize(13).Bold().FontColor(Colors.Grey.Darken4);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Toplam Günlük Ciro").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"₺{data.TotalRevenue:N2}").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                        });
                    });

                    // 2. TAHSİLAT KIRILIMI
                    col.Item().Background(Colors.Grey.Lighten4).Padding(6).Row(row =>
                    {
                        row.RelativeItem().Text($"💵 Nakit: ₺{data.CashRevenue:N2}").Bold().FontSize(8.5f);
                        row.RelativeItem().Text($"💳 Kredi Kartı: ₺{data.CreditCardRevenue:N2}").Bold().FontSize(8.5f);
                        row.RelativeItem().Text($"📝 Veresiye: ₺{data.OnAccountRevenue:N2}").Bold().FontSize(8.5f);
                    });

                    // 3. SATILAN ÜRÜNLER LİSTESİ TABLOSU
                    col.Item().Text("📦 SATILAN ÜRÜNLER LİSTESİ (ADET VE TUTAR DETAYI)").Bold().FontSize(10.5f).FontColor(Colors.Blue.Darken3);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(25);  // No
                            columns.RelativeColumn(2);   // Kod
                            columns.RelativeColumn(4);   // Ürün Adı
                            columns.RelativeColumn(2);   // Satılan Adet
                            columns.RelativeColumn(2);   // Birim Fiyat
                            columns.RelativeColumn(2);   // KDV
                            columns.RelativeColumn(2.5f);// Toplam Tutar
                            columns.RelativeColumn(1.5f);// Pay
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("#").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Ürün Kodu").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Ürün Adı").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Adet").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Birim Fiyat").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("KDV").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Toplam").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Pay").Bold().FontSize(8);
                        });

                        int rowIdx = 1;
                        foreach (var item in data.SoldProducts)
                        {
                            var bg = rowIdx % 2 == 0 ? Colors.Grey.Lighten5 : Colors.White;
                            table.Cell().Background(bg).Padding(4).Text($"{rowIdx}").FontSize(8);
                            table.Cell().Background(bg).Padding(4).Text(item.ProductCode).FontSize(8);
                            table.Cell().Background(bg).Padding(4).Text(item.ProductName).Bold().FontSize(8);
                            table.Cell().Background(bg).Padding(4).AlignRight().Text($"{item.TotalQuantity:N0}").Bold().FontColor(Colors.Green.Darken3).FontSize(8);
                            table.Cell().Background(bg).Padding(4).AlignRight().Text($"₺{item.UnitPrice:N2}").FontSize(8);
                            table.Cell().Background(bg).Padding(4).AlignRight().Text($"₺{item.VatTotal:N2}").FontSize(8);
                            table.Cell().Background(bg).Padding(4).AlignRight().Text($"₺{item.TotalRevenue:N2}").Bold().FontSize(8);
                            table.Cell().Background(bg).Padding(4).AlignRight().Text($"%{item.RevenueSharePercent:N1}").FontSize(8);
                            rowIdx++;
                        }

                        // Alt Toplam Satırı
                        table.Cell().ColumnSpan(3).Background(Colors.Grey.Lighten3).Padding(4).Text("TOPLAM SATILAN ÜRÜN VE TUTAR").Bold().FontSize(8.5f);
                        table.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text($"{data.TotalQuantitySold:N0} Adet").Bold().FontColor(Colors.Green.Darken3).FontSize(8.5f);
                        table.Cell().ColumnSpan(2).Background(Colors.Grey.Lighten3).Padding(4).Text("");
                        table.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text($"₺{data.TotalRevenue:N2}").Bold().FontColor(Colors.Blue.Darken2).FontSize(8.5f);
                        table.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("%100").Bold().FontSize(8.5f);
                    });

                    // 4. KESİLEN FİŞLERİN DÖKÜMÜ
                    col.Item().PaddingTop(5).Text("🧾 KESİLEN GÜNLÜK FİŞLER LİSTESİ").Bold().FontSize(10.5f).FontColor(Colors.Blue.Darken3);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);   // Fiş No
                            columns.RelativeColumn(2);   // Saat
                            columns.RelativeColumn(2);   // Ödeme Türü
                            columns.RelativeColumn(2);   // Kalem Sayısı
                            columns.RelativeColumn(2);   // Toplam Adet
                            columns.RelativeColumn(3);   // Tutar
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Fiş No").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Saat").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Ödeme Türü").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Kalem Çeşidi").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Toplam Adet").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Fiş Tutarı").Bold().FontSize(8);
                        });

                        int fIdx = 1;
                        foreach (var receipt in data.Receipts)
                        {
                            var bg = fIdx % 2 == 0 ? Colors.Grey.Lighten5 : Colors.White;
                            table.Cell().Background(bg).Padding(4).Text($"#{receipt.SaleId}").Bold().FontSize(8);
                            table.Cell().Background(bg).Padding(4).Text(receipt.FormattedTime).FontSize(8);
                            table.Cell().Background(bg).Padding(4).Text(receipt.PaymentType).FontSize(8);
                            table.Cell().Background(bg).Padding(4).AlignRight().Text($"{receipt.ItemCount} Kalem").FontSize(8);
                            table.Cell().Background(bg).Padding(4).AlignRight().Text($"{receipt.TotalQuantity:N0} Adet").FontSize(8);
                            table.Cell().Background(bg).Padding(4).AlignRight().Text($"₺{receipt.GrandTotal:N2}").Bold().FontSize(8);
                            fIdx++;
                        }
                    });
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    // YARDIMCI METOTLAR: ÜST BİLGİ & ALT BİLGİ & TABLO SATIRI
    private static void ComposeHeader(IContainer container, string title, string subTitle)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(10).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("ERP YÖNETİM SİSTEMİ").Bold().FontSize(16).FontColor(Colors.Blue.Darken3);
                col.Item().Text(title).Bold().FontSize(13).FontColor(Colors.Grey.Darken4);
                col.Item().Text(subTitle).FontSize(9).FontColor(Colors.Grey.Medium);
            });

            row.ConstantItem(120).AlignRight().Column(col =>
            {
                col.Item().Text("RESMİ RAPOR").Bold().FontSize(10).FontColor(Colors.Blue.Medium);
                col.Item().Text($"Basım: {DateTime.Now:dd.MM.yyyy}").FontSize(8).FontColor(Colors.Grey.Medium);
                col.Item().Text($"Saat: {DateTime.Now:HH:mm:ss}").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text("Bu belge ERP Yönetim Sistemi tarafından elektronik olarak üretilmiştir.").FontSize(7.5f).FontColor(Colors.Grey.Medium);
            row.ConstantItem(80).AlignRight().Text(x =>
            {
                x.Span("Sayfa ");
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
        });
    }

    private static void AddTableRow(TableDescriptor table, string label, string value, string? extra = null)
    {
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(label);
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(value).Bold();
        if (extra != null)
        {
            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(extra);
        }
    }
}
