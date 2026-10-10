using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ERP.Desktop.Models;
using ERP.Desktop.Services;
using Microsoft.Win32;

namespace ERP.Desktop.Views;

public partial class ReportsView : Page
{
    private readonly ReportDataService _dataService;
    private readonly PdfReportService _pdfService;
    private readonly ClosureService _closureService;
    private readonly CultureInfo _turkishCulture = new("tr-TR");

    private string _activeCategory = "DAILY"; // "DAILY" or "GENERAL"
    private string _selectedReport = "Z"; // Z, X, SALES, MONTHLY_SALES, YEARLY_SALES, MONTHLY_PRODUCTS, PROFIT_LOSS

    public System.Collections.ObjectModel.ObservableCollection<DailyClosure> Closures { get; } = new();

    public ReportsView()
    {
        InitializeComponent();
        _dataService = new ReportDataService();
        _pdfService = new PdfReportService();
        _closureService = new ClosureService();

        ClosuresGrid.ItemsSource = Closures;

        InitFilters();
        Loaded += ReportsView_Loaded;
    }

    private void InitFilters()
    {
        // 08.10.2026 silindiği için en son geçerli gün 07.10.2026 veya dündür
        var defaultDate = DateTime.Today;
        SingleDatePicker.SelectedDate = defaultDate;
        StartDatePicker.SelectedDate = new DateTime(defaultDate.Year, defaultDate.Month, 1);
        EndDatePicker.SelectedDate = defaultDate;

        for (int m = 1; m <= 12; m++)
        {
            var mName = new DateTime(2026, m, 1).ToString("MMMM", _turkishCulture);
            CmbMonth.Items.Add(new ComboBoxItem { Content = mName, Tag = m });
        }
        CmbMonth.SelectedIndex = defaultDate.Month - 1;

        int currentYear = defaultDate.Year;
        for (int y = currentYear - 2; y <= currentYear + 2; y++)
        {
            CmbYear.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });
            CmbYearOnly.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });
        }
        CmbYear.SelectedIndex = 2;     // currentYear
        CmbYearOnly.SelectedIndex = 2; // currentYear
    }

    private async void ReportsView_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateCategoryAndCardsUI();
        await RefreshPreviewDataAsync();
        await LoadClosuresAsync();
    }

    #region Category Selection (Günlük vs Genel)

    private async void CategoryDaily_Click(object sender, RoutedEventArgs e)
    {
        _activeCategory = "DAILY";
        if (_selectedReport != "Z" && _selectedReport != "X")
        {
            _selectedReport = "Z";
        }
        UpdateCategoryAndCardsUI();
        await RefreshPreviewDataAsync();
    }

    private async void CategoryGeneral_Click(object sender, RoutedEventArgs e)
    {
        _activeCategory = "GENERAL";
        if (_selectedReport == "Z" || _selectedReport == "X")
        {
            _selectedReport = "SALES";
        }
        UpdateCategoryAndCardsUI();
        await RefreshPreviewDataAsync();
    }

    private async void SelectReport_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag)
        {
            _selectedReport = tag;
            UpdateCategoryAndCardsUI();
            await RefreshPreviewDataAsync();
        }
    }

    private void UpdateCategoryAndCardsUI()
    {
        // 1. Kategori Butonları Renkleri
        if (_activeCategory == "DAILY")
        {
            BtnCategoryDaily.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            BtnCategoryDaily.Foreground = Brushes.White;
            BtnCategoryDaily.BorderThickness = new Thickness(0);

            BtnCategoryGeneral.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
            BtnCategoryGeneral.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
            BtnCategoryGeneral.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
            BtnCategoryGeneral.BorderThickness = new Thickness(1);

            PanelDailyCards.Visibility = Visibility.Visible;
            PanelGeneralCards.Visibility = Visibility.Collapsed;
        }
        else
        {
            BtnCategoryGeneral.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            BtnCategoryGeneral.Foreground = Brushes.White;
            BtnCategoryGeneral.BorderThickness = new Thickness(0);

            BtnCategoryDaily.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
            BtnCategoryDaily.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
            BtnCategoryDaily.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
            BtnCategoryDaily.BorderThickness = new Thickness(1);

            PanelDailyCards.Visibility = Visibility.Collapsed;
            PanelGeneralCards.Visibility = Visibility.Visible;
        }

        // 2. Rapor Kartlarının Aktif Seçimi
        var allCards = new[] { CardZReport, CardXReport, CardSalesReport, CardMonthlySales, CardYearlySales, CardMonthlyProducts, CardProfitLoss };
        foreach (var c in allCards)
        {
            if (c.Tag?.ToString() == _selectedReport)
            {
                c.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                c.BorderThickness = new Thickness(2);
                c.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
            }
            else
            {
                c.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                c.BorderThickness = new Thickness(1);
                c.Background = Brushes.White;
            }
        }

        // 3. İlgili Filtre Çubuklarının Görünürlüğü
        PanelFilterDaily.Visibility = Visibility.Collapsed;
        PanelFilterRange.Visibility = Visibility.Collapsed;
        PanelFilterMonth.Visibility = Visibility.Collapsed;
        PanelFilterYear.Visibility = Visibility.Collapsed;

        BtnTakeClosureAction.Visibility = (_selectedReport == "Z" || _selectedReport == "X") ? Visibility.Visible : Visibility.Collapsed;

        switch (_selectedReport)
        {
            case "Z":
                PanelFilterDaily.Visibility = Visibility.Visible;
                TxtSelectedReportTitle.Text = "Seçili: ⚡ Z Raporu (Gün Sonu Resmî Kapanış)";
                TxtSelectedReportSubtitle.Text = $"Tarih: {SingleDatePicker.SelectedDate:dd.MM.yyyy} | Kesinleşmiş mali kapanış özeti";
                break;

            case "X":
                PanelFilterDaily.Visibility = Visibility.Visible;
                TxtSelectedReportTitle.Text = "Seçili: 🕒 X Raporu (Gün Ortası Canlı Kasa)";
                TxtSelectedReportSubtitle.Text = $"Tarih: {SingleDatePicker.SelectedDate:dd.MM.yyyy} | Anlık ara durum (Mali hafızayı sıfırlamaz)";
                break;

            case "SALES":
                PanelFilterRange.Visibility = Visibility.Visible;
                TxtSelectedReportTitle.Text = "Seçili: 📑 Detaylı Satış Raporu";
                TxtSelectedReportSubtitle.Text = $"Dönem: {StartDatePicker.SelectedDate:dd.MM.yyyy} - {EndDatePicker.SelectedDate:dd.MM.yyyy} | Fiş fiş satış dökümü";
                break;

            case "MONTHLY_SALES":
                PanelFilterMonth.Visibility = Visibility.Visible;
                TxtSelectedReportTitle.Text = "Seçili: 📈 Aylık Ciro Raporu (Gün Gün Dağılım)";
                TxtSelectedReportSubtitle.Text = $"Dönem: {GetSelectedMonthName()} {GetSelectedYear()} | Gün gün ciro tablosu ve grafiği";
                break;

            case "YEARLY_SALES":
                PanelFilterYear.Visibility = Visibility.Visible;
                TxtSelectedReportTitle.Text = "Seçili: 📆 Yıllık Ciro Raporu (Ay Ay Dağılım)";
                TxtSelectedReportSubtitle.Text = $"Yıl: {GetSelectedYearOnly()} | Ay ay ciro tablosu ve yıllık büyüme grafiği";
                break;

            case "MONTHLY_PRODUCTS":
                PanelFilterMonth.Visibility = Visibility.Visible;
                TxtSelectedReportTitle.Text = "Seçili: 📦 Aylık Ürün Satış ve Performans Raporu";
                TxtSelectedReportSubtitle.Text = $"Dönem: {GetSelectedMonthName()} {GetSelectedYear()} | Çok satan ürünler ve kârlılık dökümü";
                break;

            case "PROFIT_LOSS":
                PanelFilterRange.Visibility = Visibility.Visible;
                TxtSelectedReportTitle.Text = "Seçili: ⚖️ Kâr / Zarar Raporu (Gelir - Gider)";
                TxtSelectedReportSubtitle.Text = $"Dönem: {StartDatePicker.SelectedDate:dd.MM.yyyy} - {EndDatePicker.SelectedDate:dd.MM.yyyy} | Mali tablo ve kârlılık marjı";
                break;
        }
    }

    private int GetSelectedMonth() => (CmbMonth.SelectedItem as ComboBoxItem)?.Tag as int? ?? DateTime.Today.Month;
    private int GetSelectedYear() => (CmbYear.SelectedItem as ComboBoxItem)?.Tag as int? ?? DateTime.Today.Year;
    private int GetSelectedYearOnly() => (CmbYearOnly.SelectedItem as ComboBoxItem)?.Tag as int? ?? DateTime.Today.Year;
    private string GetSelectedMonthName() => (CmbMonth.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

    #endregion

    #region Data Loading & Chart Generation

    private async void BtnFilterReports_Click(object sender, RoutedEventArgs e)
    {
        UpdateCategoryAndCardsUI();
        await RefreshPreviewDataAsync();
    }

    private async void RefreshData_Click(object sender, RoutedEventArgs e)
    {
        UpdateCategoryAndCardsUI();
        await RefreshPreviewDataAsync();
    }

    private async Task RefreshPreviewDataAsync()
    {
        try
        {
            switch (_selectedReport)
            {
                case "Z":
                    await LoadZReportPreviewAsync();
                    break;

                case "X":
                    await LoadXReportPreviewAsync();
                    break;

                case "SALES":
                    await LoadSalesReportPreviewAsync();
                    break;

                case "MONTHLY_SALES":
                    await LoadMonthlySalesReportPreviewAsync();
                    break;

                case "YEARLY_SALES":
                    await LoadYearlySalesReportPreviewAsync();
                    break;

                case "MONTHLY_PRODUCTS":
                    await LoadMonthlyProductReportPreviewAsync();
                    break;

                case "PROFIT_LOSS":
                    await LoadProfitLossReportPreviewAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Önizleme verisi yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // 1. Z Raporu
    private async Task LoadZReportPreviewAsync()
    {
        var targetDate = SingleDatePicker.SelectedDate ?? DateTime.Today;
        var zData = await _dataService.GetZReportDataAsync(targetDate);
        if (zData != null)
        {
            TxtPreviewHeader.Text = $"⚡ Z Raporu Özeti (Kapanış No: Z-{zData.ClosureId:D6})";
            PreviewDataGrid.ItemsSource = new[]
            {
                new { Parametre = "Kapanış Tarihi ve Saati", Deger = $"{zData.ClosureDate:dd.MM.yyyy} {zData.ClosedAt:HH:mm:ss}" },
                new { Parametre = "Toplam Satış / Fiş Adedi", Deger = $"{zData.SalesCount} Fiş" },
                new { Parametre = "Toplam Günlük Ciro", Deger = $"₺{zData.TotalRevenue:N2}" },
                new { Parametre = "Nakit Tahsilat", Deger = $"₺{zData.TotalCash:N2}" },
                new { Parametre = "Kredi Kartı Tahsilat", Deger = $"₺{zData.TotalCreditCard:N2}" },
                new { Parametre = "Veresiye Tahsilat", Deger = $"₺{zData.TotalOnAccount:N2}" },
                new { Parametre = "Satılan Malların Maliyeti", Deger = $"₺{zData.TotalCost:N2}" },
                new { Parametre = "Kaydedilen Giderler", Deger = $"₺{zData.TotalExpenses:N2}" },
                new { Parametre = "Net Günlük Kâr", Deger = $"₺{zData.TotalProfit:N2}" },
                new { Parametre = "Kasa Başlangıç Nakiti", Deger = $"₺{zData.OpeningCash:N2}" }
            };

            // Grafik & KPI
            TxtChartTitle.Text = $"⚡ {zData.ClosureDate:dd.MM.yyyy} Z Raporu Tahsilat ve Gelir/Gider Sütun Grafiği";
            TxtKpiTotalLabel.Text = "Toplam Ciro";
            TxtKpiTotalValue.Text = $"₺{zData.TotalRevenue:N2}";
            TxtKpiAvgLabel.Text = "Ortalama Fiş";
            decimal avgReceipt = zData.SalesCount > 0 ? zData.TotalRevenue / zData.SalesCount : 0m;
            TxtKpiAvgValue.Text = $"₺{avgReceipt:N2}";
            TxtKpiExtraLabel.Text = "Net Kâr";
            TxtKpiExtraValue.Text = $"₺{zData.TotalProfit:N2}";

            var chartItems = new List<ReportChartItem>
            {
                new() { Label = "Nakit", SubLabel = "Tahsilat", Value = zData.TotalCash, DisplayValue = $"₺{zData.TotalCash:N0}", BarColor = "#10B981", Tooltip = $"Nakit: ₺{zData.TotalCash:N2}" },
                new() { Label = "Kredi K.", SubLabel = "Tahsilat", Value = zData.TotalCreditCard, DisplayValue = $"₺{zData.TotalCreditCard:N0}", BarColor = "#2563EB", Tooltip = $"Kredi Kartı: ₺{zData.TotalCreditCard:N2}" },
                new() { Label = "Veresiye", SubLabel = "Açık Hesap", Value = zData.TotalOnAccount, DisplayValue = $"₺{zData.TotalOnAccount:N0}", BarColor = "#F59E0B", Tooltip = $"Veresiye: ₺{zData.TotalOnAccount:N2}" },
                new() { Label = "Maliyet", SubLabel = "STMM", Value = zData.TotalCost, DisplayValue = $"₺{zData.TotalCost:N0}", BarColor = "#EF4444", Tooltip = $"Maliyet: ₺{zData.TotalCost:N2}" },
                new() { Label = "Giderler", SubLabel = "Masraf", Value = zData.TotalExpenses, DisplayValue = $"₺{zData.TotalExpenses:N0}", BarColor = "#DC2626", Tooltip = $"Giderler: ₺{zData.TotalExpenses:N2}" },
                new() { Label = "Net Kâr", SubLabel = "Dönem Kârı", Value = Math.Max(0, zData.TotalProfit), DisplayValue = $"₺{zData.TotalProfit:N0}", BarColor = "#059669", Tooltip = $"Net Kâr: ₺{zData.TotalProfit:N2}" }
            };

            RenderChartBars(chartItems);
        }
        else
        {
            TxtPreviewHeader.Text = "⚡ Seçilen tarihe ait kayıtlı Z Raporu bulunamadı.";
            PreviewDataGrid.ItemsSource = null;
            ClearChartUI("⚡ Kayıtlı Z Raporu Bulunmuyor");
        }
    }

    // 2. X Raporu
    private async Task LoadXReportPreviewAsync()
    {
        var targetDate = SingleDatePicker.SelectedDate ?? DateTime.Today;
        var xData = await _dataService.GetXReportDataAsync(targetDate);
        string statusText = xData.IsCashRegisterOpen ? "🟢 Kasa Açık" : "🔴 Kasa Kapalı (Gün Sonu Yapıldı - Sıfırlandı)";
        TxtPreviewHeader.Text = xData.IsCashRegisterOpen 
            ? $"🕒 X Raporu Canlı Durum ({xData.ReportTime:HH:mm:ss})"
            : $"🕒 X Raporu - Gün Sonu Yapıldı ({xData.ReportTime:HH:mm:ss}) - Ara Değerler Sıfırlandı";

        PreviewDataGrid.ItemsSource = new[]
        {
            new { Parametre = "Kasa Durumu", Deger = statusText },
            new { Parametre = "Kasa Açılış Saati", Deger = xData.CashOpenedAt?.ToString("HH:mm:ss") ?? "-" },
            new { Parametre = "Kasa Başlangıç Nakiti", Deger = $"₺{xData.OpeningCash:N2}" },
            new { Parametre = "Tamamlanan Satışlar", Deger = $"{xData.SalesCount} Adet Fiş" },
            new { Parametre = "Gün Ortası Toplam Ciro", Deger = $"₺{xData.TotalRevenue:N2}" },
            new { Parametre = "Günün Nakit Satışları", Deger = $"₺{xData.CashSales:N2}" },
            new { Parametre = "Günün Kartlı Satışları", Deger = $"₺{xData.CreditCardSales:N2}" },
            new { Parametre = "Manuel Kasa Girişi", Deger = $"+₺{xData.ManualCashIn:N2}" },
            new { Parametre = "Manuel Kasadan Çıkış / Masraf", Deger = $"-₺{xData.ManualCashOut:N2}" },
            new { Parametre = "Kasada Bulunması Gereken Nakit", Deger = $"₺{xData.CurrentCashBalance:N2}" },
            new { Parametre = "Gün Ortası Tahmini Kâr", Deger = $"₺{xData.EstimatedProfit:N2}" }
        };

        // Grafik & KPI
        TxtChartTitle.Text = $"🕒 {xData.TargetDate:dd.MM.yyyy} X Raporu Gün Ortası Kasa & Satış Dağılımı Sütun Grafiği";
        TxtKpiTotalLabel.Text = "Gün Ortası Ciro";
        TxtKpiTotalValue.Text = $"₺{xData.TotalRevenue:N2}";
        TxtKpiAvgLabel.Text = "Kasadaki Nakit";
        TxtKpiAvgValue.Text = $"₺{xData.CurrentCashBalance:N2}";
        TxtKpiExtraLabel.Text = "Tahmini Kâr";
        TxtKpiExtraValue.Text = $"₺{xData.EstimatedProfit:N2}";

        var chartItems = new List<ReportChartItem>
        {
            new() { Label = "Nakit", SubLabel = "Satış", Value = xData.CashSales, DisplayValue = $"₺{xData.CashSales:N0}", BarColor = "#10B981", Tooltip = $"Nakit Satış: ₺{xData.CashSales:N2}" },
            new() { Label = "Kredi K.", SubLabel = "Satış", Value = xData.CreditCardSales, DisplayValue = $"₺{xData.CreditCardSales:N0}", BarColor = "#2563EB", Tooltip = $"Kartlı Satış: ₺{xData.CreditCardSales:N2}" },
            new() { Label = "Veresiye", SubLabel = "Satış", Value = xData.OnAccountSales, DisplayValue = $"₺{xData.OnAccountSales:N0}", BarColor = "#F59E0B", Tooltip = $"Veresiye: ₺{xData.OnAccountSales:N2}" },
            new() { Label = "Giriş", SubLabel = "Manuel", Value = xData.ManualCashIn, DisplayValue = $"₺{xData.ManualCashIn:N0}", BarColor = "#3B82F6", Tooltip = $"Kasa Giriş: ₺{xData.ManualCashIn:N2}" },
            new() { Label = "Çıkış", SubLabel = "Masraf", Value = xData.ManualCashOut, DisplayValue = $"₺{xData.ManualCashOut:N0}", BarColor = "#EF4444", Tooltip = $"Kasa Çıkış: ₺{xData.ManualCashOut:N2}" },
            new() { Label = "Kasa Nakit", SubLabel = "Mevcut", Value = xData.CurrentCashBalance, DisplayValue = $"₺{xData.CurrentCashBalance:N0}", BarColor = "#059669", Tooltip = $"Kasada Nakit: ₺{xData.CurrentCashBalance:N2}" }
        };

        RenderChartBars(chartItems);
    }

    // 3. Detaylı Satış Raporu (Tarih Aralığı)
    private async Task LoadSalesReportPreviewAsync()
    {
        var start = StartDatePicker.SelectedDate ?? DateTime.Today;
        var end = EndDatePicker.SelectedDate ?? DateTime.Today;
        var salesData = await _dataService.GetSalesReportDataAsync(start, end);
        TxtPreviewHeader.Text = $"📑 Detaylı Satış Fişleri ({salesData.TotalSalesCount} Fiş | Toplam Ciro: ₺{salesData.TotalRevenue:N2})";
        PreviewDataGrid.ItemsSource = salesData.Items.Select(x => new
        {
            FisNo = $"#{x.SaleId}",
            TarihSaat = x.SaleDate.ToString("dd.MM.yyyy HH:mm"),
            OdemeTuru = x.PaymentType,
            UrunCesidi = $"{x.ItemCount} Kalem",
            AraToplam = $"₺{x.SubTotal:N2}",
            KDV = $"₺{x.VatTotal:N2}",
            GenelToplam = $"₺{x.GrandTotal:N2}",
            TahminiKar = $"₺{x.Profit:N2}"
        }).ToList();

        // Gün gün grupla
        var dayGroups = salesData.Items
            .GroupBy(x => x.SaleDate.Date)
            .OrderBy(g => g.Key)
            .ToList();

        int totalDays = (int)(end.Date - start.Date).TotalDays + 1;
        if (totalDays <= 0) totalDays = 1;
        decimal dailyAvg = salesData.TotalRevenue / totalDays;

        var bestGroup = dayGroups.OrderByDescending(g => g.Sum(x => x.GrandTotal)).FirstOrDefault();
        decimal maxDayTotal = bestGroup?.Sum(x => x.GrandTotal) ?? 0m;
        string maxDayStr = bestGroup != null ? $"{bestGroup.Key:dd.MM} (₺{maxDayTotal:N0})" : "-";

        TxtChartTitle.Text = $"📑 {start:dd.MM.yyyy} - {end:dd.MM.yyyy} Gün Gün Satış Ciro Sütun Grafiği";
        TxtKpiTotalLabel.Text = "Toplam Ciro";
        TxtKpiTotalValue.Text = $"₺{salesData.TotalRevenue:N2}";
        TxtKpiAvgLabel.Text = "Günlük Ortalama";
        TxtKpiAvgValue.Text = $"₺{dailyAvg:N2}";
        TxtKpiExtraLabel.Text = "En Yüksek Gün";
        TxtKpiExtraValue.Text = maxDayStr;

        var chartItems = new List<ReportChartItem>();
        for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
        {
            var match = dayGroups.FirstOrDefault(g => g.Key == d);
            decimal total = match?.Sum(x => x.GrandTotal) ?? 0m;
            int count = match?.Count() ?? 0;

            chartItems.Add(new ReportChartItem
            {
                Label = d.ToString("dd.MM"),
                SubLabel = d.ToString("ddd", _turkishCulture),
                Value = total,
                DisplayValue = total > 0 ? $"₺{total:N0}" : "-",
                BarColor = total == maxDayTotal && total > 0 ? "#10B981" : "#2563EB",
                Tooltip = $"{d:dd.MM.yyyy dddd}\nCiro: ₺{total:N2}\nSatış Adedi: {count} Fiş"
            });
        }

        RenderChartBars(chartItems);
    }

    // 4. Aylık Ciro Raporu (Ay / Yıl)
    private async Task LoadMonthlySalesReportPreviewAsync()
    {
        int m = GetSelectedMonth();
        int y = GetSelectedYear();
        var mSales = await _dataService.GetMonthlySalesReportDataAsync(y, m);
        TxtPreviewHeader.Text = $"📈 {mSales.MonthName} Günlük Ciro Tablosu (Toplam Ciro: ₺{mSales.GrandTotal:N2})";
        PreviewDataGrid.ItemsSource = mSales.Days.Select(d => new
        {
            Tarih = d.Date.ToString("dd.MM.yyyy dddd", _turkishCulture),
            FisAdedi = d.SalesCount > 0 ? $"{d.SalesCount}" : "-",
            Nakit = $"₺{d.CashTotal:N2}",
            KrediKarti = $"₺{d.CardTotal:N2}",
            Veresiye = $"₺{d.OnAccountTotal:N2}",
            GunlukCiro = $"₺{d.DailyTotal:N2}",
            KumulatifCiro = $"₺{d.CumulativeTotal:N2}"
        }).ToList();

        // Grafik & KPI
        TxtChartTitle.Text = $"📈 {mSales.MonthName} Gün Gün Ciro Sütun Grafiği";
        TxtKpiTotalLabel.Text = "Ay Toplam Ciro";
        TxtKpiTotalValue.Text = $"₺{mSales.GrandTotal:N2}";
        TxtKpiAvgLabel.Text = "Günlük Ortalama";
        TxtKpiAvgValue.Text = $"₺{mSales.DailyAverage:N2}";
        TxtKpiExtraLabel.Text = "En Yüksek Gün";
        TxtKpiExtraValue.Text = mSales.BestDay.HasValue ? $"{mSales.BestDay.Value:dd MMMM} (₺{mSales.BestDayTotal:N0})" : "-";

        var chartItems = mSales.Days.Select(d => new ReportChartItem
        {
            Label = d.Date.Day.ToString("D2"),
            SubLabel = d.Date.ToString("ddd", _turkishCulture),
            Value = d.DailyTotal,
            DisplayValue = d.DailyTotal > 0 ? $"₺{d.DailyTotal:N0}" : "-",
            BarColor = (d.DailyTotal == mSales.BestDayTotal && d.DailyTotal > 0) ? "#10B981" : "#3B82F6",
            Tooltip = $"{d.Date:dd.MM.yyyy dddd}\nToplam Ciro: ₺{d.DailyTotal:N2}\nNakit: ₺{d.CashTotal:N2}\nKart: ₺{d.CardTotal:N2}\nFiş: {d.SalesCount}"
        }).ToList();

        RenderChartBars(chartItems);
    }

    // 5. Yıllık Ciro Raporu (Yıl Bazlı) - YENİ!
    private async Task LoadYearlySalesReportPreviewAsync()
    {
        int y = GetSelectedYearOnly();
        var yData = await _dataService.GetYearlyRevenueReportDataAsync(y);
        TxtPreviewHeader.Text = $"📆 {yData.Year} Yılı Ay Ay Ciro ve Büyüme Tablosu (Yıllık Ciro: ₺{yData.GrandTotal:N2})";
        PreviewDataGrid.ItemsSource = yData.Months.Select(m => new
        {
            Ay = m.MonthName,
            FisAdedi = m.SalesCount > 0 ? $"{m.SalesCount}" : "-",
            NakitTahsilat = $"₺{m.CashTotal:N2}",
            KrediKarti = $"₺{m.CardTotal:N2}",
            Veresiye = $"₺{m.OnAccountTotal:N2}",
            AylikCiro = $"₺{m.MonthlyTotal:N2}",
            KumulatifCiro = $"₺{m.CumulativeTotal:N2}"
        }).ToList();

        // Grafik & KPI
        TxtChartTitle.Text = $"📆 {yData.Year} Yılı Ay Ay Ciro Sütun Grafiği";
        TxtKpiTotalLabel.Text = "Yıllık Toplam Ciro";
        TxtKpiTotalValue.Text = $"₺{yData.GrandTotal:N2}";
        TxtKpiAvgLabel.Text = "Aylık Ortalama Ciro";
        TxtKpiAvgValue.Text = $"₺{yData.MonthlyAverage:N2}";
        TxtKpiExtraLabel.Text = "En Yüksek Ay";
        TxtKpiExtraValue.Text = !string.IsNullOrEmpty(yData.BestMonthName) && yData.BestMonthTotal > 0 
            ? $"{yData.BestMonthName} (₺{yData.BestMonthTotal:N0})" 
            : "-";

        var chartItems = yData.Months.Select(m => new ReportChartItem
        {
            Label = m.MonthName.Length >= 3 ? m.MonthName.Substring(0, 3) : m.MonthName,
            SubLabel = $"{m.Month:D2}",
            Value = m.MonthlyTotal,
            DisplayValue = m.MonthlyTotal > 0 ? $"₺{m.MonthlyTotal:N0}" : "-",
            BarColor = (m.MonthlyTotal == yData.BestMonthTotal && m.MonthlyTotal > 0) ? "#10B981" : "#4F46E5",
            Tooltip = $"{m.MonthName} {yData.Year}\nAylık Ciro: ₺{m.MonthlyTotal:N2}\nNakit: ₺{m.CashTotal:N2}\nKart: ₺{m.CardTotal:N2}\nToplam Fiş: {m.SalesCount}"
        }).ToList();

        RenderChartBars(chartItems);
    }

    // 6. Aylık Ürün Satış Raporu
    private async Task LoadMonthlyProductReportPreviewAsync()
    {
        int pm = GetSelectedMonth();
        int py = GetSelectedYear();
        var pSales = await _dataService.GetMonthlyProductReportDataAsync(py, pm);
        TxtPreviewHeader.Text = $"📦 {pSales.MonthName} Ürün Satış Performansı ({pSales.Products.Count} Çeşit Ürün | Toplam Satılan: {pSales.TotalQuantitySold} Adet)";
        PreviewDataGrid.ItemsSource = pSales.Products.Select(p => new
        {
            Sira = p.Rank,
            Kod = p.ProductCode,
            UrunAdi = p.ProductName,
            SatilanAdet = p.TotalQuantity,
            OrtBirimFiyat = $"₺{p.AverageUnitPrice:N2}",
            ToplamCiro = $"₺{p.TotalRevenue:N2}",
            ToplamKar = $"₺{p.Profit:N2}",
            CiroPayi = $"%{p.RevenueSharePercent:N1}"
        }).ToList();

        // Grafik & KPI
        TxtChartTitle.Text = $"📦 {pSales.MonthName} En Çok Ciro Getiren Ürünler Sütun Grafiği (İlk 12 Ürün)";
        TxtKpiTotalLabel.Text = "Ürün Toplam Ciro";
        TxtKpiTotalValue.Text = $"₺{pSales.TotalRevenue:N2}";
        decimal avgProdRev = pSales.Products.Count > 0 ? pSales.TotalRevenue / pSales.Products.Count : 0m;
        TxtKpiAvgLabel.Text = "Ürün Ort. Ciro";
        TxtKpiAvgValue.Text = $"₺{avgProdRev:N2}";
        var topProduct = pSales.Products.FirstOrDefault();
        TxtKpiExtraLabel.Text = "En Çok Satan";
        TxtKpiExtraValue.Text = topProduct != null ? $"{topProduct.ProductName} ({topProduct.TotalQuantity:N0} Adet)" : "-";

        // İlk 12 ürünü sütun grafiğinde göster
        var top12 = pSales.Products.Take(12).ToList();
        var chartItems = top12.Select(p => new ReportChartItem
        {
            Label = p.ProductName.Length > 8 ? p.ProductName.Substring(0, 8) + ".." : p.ProductName,
            SubLabel = $"#{p.Rank}",
            Value = p.TotalRevenue,
            DisplayValue = $"₺{p.TotalRevenue:N0}",
            BarColor = p.Rank == 1 ? "#10B981" : "#0891B2",
            Tooltip = $"{p.ProductName} (#{p.Rank})\nToplam Ciro: ₺{p.TotalRevenue:N2}\nSatılan Adet: {p.TotalQuantity}\nKâr: ₺{p.Profit:N2}\nCiro Payı: %{p.RevenueSharePercent:N1}"
        }).ToList();

        RenderChartBars(chartItems);
    }

    // 7. Kâr / Zarar Raporu
    private async Task LoadProfitLossReportPreviewAsync()
    {
        var plStart = StartDatePicker.SelectedDate ?? DateTime.Today;
        var plEnd = EndDatePicker.SelectedDate ?? DateTime.Today;
        var plData = await _dataService.GetProfitLossReportDataAsync(plStart, plEnd);
        TxtPreviewHeader.Text = $"⚖️ Kâr / Zarar Gelir-Gider Tablosu ({plData.PeriodTitle})";
        PreviewDataGrid.ItemsSource = new[]
        {
            new { Kalem = "1. Brüt Satış Gelirleri (Ciro)", Tutar = $"₺{plData.GrossRevenue:N2}", Aciklama = "Dönem içindeki toplam satışlar" },
            new { Kalem = "2. Hesaplanan KDV", Tutar = $"-₺{plData.VatTotal:N2}", Aciklama = "Devlete ödenecek KDV" },
            new { Kalem = "3. Net Satış Geliri", Tutar = $"₺{plData.NetSalesRevenue:N2}", Aciklama = "KDV hariç net ciro" },
            new { Kalem = "4. Satılan Malların Maliyeti (STMM)", Tutar = $"-₺{plData.CostOfGoodsSold:N2}", Aciklama = "Satılan ürünlerin alış maliyetleri" },
            new { Kalem = "5. BRÜT FAALİYET KÂRI", Tutar = $"₺{plData.GrossProfit:N2}", Aciklama = "Ciro - Mal Maliyeti" },
            new { Kalem = "6. Genel İşletme Giderleri & Masraflar", Tutar = $"-₺{plData.TotalOperatingExpenses:N2}", Aciklama = "Kasa çıkış masrafları ve giderler" },
            new { Kalem = "7. DÖNEM NET FAALİYET KÂRI / ZARARI", Tutar = $"₺{plData.NetProfitOrLoss:N2}", Aciklama = $"Net kârlılık marjı: %{plData.ProfitMarginPercent:N2}" }
        };

        // Grafik & KPI
        TxtChartTitle.Text = $"⚖️ {plData.PeriodTitle} Finansal Gelir, Gider ve Net Kâr Sütun Grafiği";
        TxtKpiTotalLabel.Text = "Brüt Satış Geliri";
        TxtKpiTotalValue.Text = $"₺{plData.GrossRevenue:N2}";
        TxtKpiAvgLabel.Text = "Net Kâr Marjı";
        TxtKpiAvgValue.Text = $"%{plData.ProfitMarginPercent:N2}";
        TxtKpiExtraLabel.Text = "Net Dönem Kârı";
        TxtKpiExtraValue.Text = $"₺{plData.NetProfitOrLoss:N2}";

        var chartItems = new List<ReportChartItem>
        {
            new() { Label = "Brüt Ciro", SubLabel = "Gelir", Value = plData.GrossRevenue, DisplayValue = $"₺{plData.GrossRevenue:N0}", BarColor = "#2563EB", Tooltip = $"Brüt Ciro: ₺{plData.GrossRevenue:N2}" },
            new() { Label = "KDV", SubLabel = "Vergi", Value = plData.VatTotal, DisplayValue = $"₺{plData.VatTotal:N0}", BarColor = "#F59E0B", Tooltip = $"KDV: ₺{plData.VatTotal:N2}" },
            new() { Label = "Maliyet", SubLabel = "STMM", Value = plData.CostOfGoodsSold, DisplayValue = $"₺{plData.CostOfGoodsSold:N0}", BarColor = "#EF4444", Tooltip = $"Mal Maliyeti: ₺{plData.CostOfGoodsSold:N2}" },
            new() { Label = "Brüt Kâr", SubLabel = "Faaliyet", Value = Math.Max(0, plData.GrossProfit), DisplayValue = $"₺{plData.GrossProfit:N0}", BarColor = "#10B981", Tooltip = $"Brüt Faaliyet Kârı: ₺{plData.GrossProfit:N2}" },
            new() { Label = "Giderler", SubLabel = "İşletme", Value = plData.TotalOperatingExpenses, DisplayValue = $"₺{plData.TotalOperatingExpenses:N0}", BarColor = "#DC2626", Tooltip = $"İşletme Giderleri: ₺{plData.TotalOperatingExpenses:N2}" },
            new() { Label = "Net Kâr", SubLabel = "Sonuç", Value = Math.Max(0, plData.NetProfitOrLoss), DisplayValue = $"₺{plData.NetProfitOrLoss:N0}", BarColor = plData.NetProfitOrLoss >= 0 ? "#059669" : "#B91C1C", Tooltip = $"Net Kâr: ₺{plData.NetProfitOrLoss:N2}" }
        };

        RenderChartBars(chartItems);
    }

    private void RenderChartBars(List<ReportChartItem> items)
    {
        if (items.Count == 0)
        {
            ChartItemsControl.ItemsSource = null;
            return;
        }

        decimal maxVal = items.Max(x => x.Value);
        if (maxVal <= 0) maxVal = 1;

        const double maxBarHeight = 150.0;
        const double minBarHeight = 6.0;

        foreach (var item in items)
        {
            if (item.Value > 0)
            {
                double calculated = (double)(item.Value / maxVal) * maxBarHeight;
                item.BarHeight = Math.Max(minBarHeight, calculated);
            }
            else
            {
                item.BarHeight = 4.0;
            }
        }

        ChartItemsControl.ItemsSource = items;
    }

    private void ClearChartUI(string message)
    {
        TxtChartTitle.Text = message;
        TxtKpiTotalValue.Text = "₺0,00";
        TxtKpiAvgValue.Text = "₺0,00";
        TxtKpiExtraValue.Text = "-";
        ChartItemsControl.ItemsSource = null;
    }

    #endregion

    #region PDF Generation & Actions

    private async void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        await GenerateAndHandlePdfAsync(openImmediately: true);
    }

    private async void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        await GenerateAndHandlePdfAsync(openImmediately: false);
    }

    private async Task GenerateAndHandlePdfAsync(bool openImmediately)
    {
        try
        {
            string? destinationPath = null;

            if (!openImmediately)
            {
                var sfd = new SaveFileDialog
                {
                    Filter = "PDF Belgesi (*.pdf)|*.pdf",
                    FileName = $"{_selectedReport}_Raporu_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
                };
                if (sfd.ShowDialog() == true)
                {
                    destinationPath = sfd.FileName;
                }
                else
                {
                    return;
                }
            }

            string generatedFile = "";

            switch (_selectedReport)
            {
                case "Z":
                    var zData = await _dataService.GetZReportDataAsync(SingleDatePicker.SelectedDate ?? DateTime.Today);
                    if (zData == null)
                    {
                        MessageBox.Show("Seçilen tarihe ait kaydedilmiş gün sonu (Z Raporu) bulunamadı.\nLütfen önce gün sonu alın veya geçmiş bir tarihi seçin.", "Kayıt Yok", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    generatedFile = _pdfService.GenerateZReportPdf(zData, destinationPath);
                    break;

                case "X":
                    var xData = await _dataService.GetXReportDataAsync(SingleDatePicker.SelectedDate ?? DateTime.Today);
                    generatedFile = _pdfService.GenerateXReportPdf(xData, destinationPath);
                    break;

                case "SALES":
                    var start = StartDatePicker.SelectedDate ?? DateTime.Today;
                    var end = EndDatePicker.SelectedDate ?? DateTime.Today;
                    var sData = await _dataService.GetSalesReportDataAsync(start, end);
                    generatedFile = _pdfService.GenerateSalesReportPdf(sData, destinationPath);
                    break;

                case "MONTHLY_SALES":
                    int m = GetSelectedMonth();
                    int y = GetSelectedYear();
                    var msData = await _dataService.GetMonthlySalesReportDataAsync(y, m);
                    generatedFile = _pdfService.GenerateMonthlySalesReportPdf(msData, destinationPath);
                    break;

                case "YEARLY_SALES":
                    int yOnly = GetSelectedYearOnly();
                    var yrData = await _dataService.GetYearlyRevenueReportDataAsync(yOnly);
                    generatedFile = _pdfService.GenerateYearlySalesReportPdf(yrData, destinationPath);
                    break;

                case "MONTHLY_PRODUCTS":
                    int pm = GetSelectedMonth();
                    int py = GetSelectedYear();
                    var mpData = await _dataService.GetMonthlyProductReportDataAsync(py, pm);
                    generatedFile = _pdfService.GenerateMonthlyProductReportPdf(mpData, destinationPath);
                    break;

                case "PROFIT_LOSS":
                    var plStart = StartDatePicker.SelectedDate ?? DateTime.Today;
                    var plEnd = EndDatePicker.SelectedDate ?? DateTime.Today;
                    var plData = await _dataService.GetProfitLossReportDataAsync(plStart, plEnd);
                    generatedFile = _pdfService.GenerateProfitLossReportPdf(plData, destinationPath);
                    break;
            }

            if (!string.IsNullOrEmpty(generatedFile))
            {
                if (openImmediately)
                {
                    PdfReportService.OpenPdfFile(generatedFile);
                }
                else
                {
                    var ask = MessageBox.Show($"PDF raporu başarıyla kaydedildi:\n{generatedFile}\n\nDosyayı şimdi açmak ister misiniz?", "PDF Kaydedildi", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (ask == MessageBoxResult.Yes)
                    {
                        PdfReportService.OpenPdfFile(generatedFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PDF oluşturulurken bir hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Geçmiş Gün Sonu Arşivi (Tab 2)

    private List<DailyClosure> _allClosures = new();

    private async Task LoadClosuresAsync()
    {
        try
        {
            _allClosures = await _closureService.GetClosuresAsync();
            ApplyClosureFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Geçmiş gün sonları yüklenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyClosureFilter(bool onlyIban = false)
    {
        Closures.Clear();
        var query = _allClosures.AsEnumerable();

        if (FilterClosureDate.SelectedDate.HasValue)
        {
            query = query.Where(c => c.ClosureDate.Date == FilterClosureDate.SelectedDate.Value.Date);
        }

        if (onlyIban)
        {
            query = query.Where(c => c.IbanAmount > 0);
        }

        foreach (var item in query)
        {
            Closures.Add(item);
        }
    }

    private void FilterClosureDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyClosureFilter();
    }

    private void ClearClosureFilter_Click(object sender, RoutedEventArgs e)
    {
        FilterClosureDate.SelectedDate = null;
        ApplyClosureFilter();
    }

    private void FilterOnlyIban_Click(object sender, RoutedEventArgs e)
    {
        FilterClosureDate.SelectedDate = null;
        ApplyClosureFilter(onlyIban: true);
    }

    private void OpenRowReceiptBreakdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DailyClosure closure)
        {
            try
            {
                var breakdownWin = new DailyReceiptBreakdownWindow(closure.ClosureDate);
                breakdownWin.Owner = Window.GetWindow(this);
                breakdownWin.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fiş dökümü açılırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void RefreshClosures_Click(object sender, RoutedEventArgs e)
    {
        await LoadClosuresAsync();
    }

    private async void OpenRowZReport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DailyClosure closure)
        {
            try
            {
                _activeCategory = "DAILY";
                _selectedReport = "Z";
                SingleDatePicker.SelectedDate = closure.ClosureDate;
                UpdateCategoryAndCardsUI();

                var zData = await _dataService.GetZReportDataAsync(closure.ClosureDate);
                if (zData == null)
                {
                    MessageBox.Show("Bu güne ait Z Raporu verisi bulunamadı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var filePath = _pdfService.GenerateZReportPdf(zData);
                PdfReportService.OpenPdfFile(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Z Raporu açılırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void TakeClosureFromReports_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var targetDate = SingleDatePicker.SelectedDate ?? DateTime.Today;

            var summary = await _closureService.CalculateDailySummaryAsync(targetDate);
            var cashService = new CashService();
            var cashSummary = await cashService.GetCashSummaryAsync(targetDate);

            var confirmMsg = $"📅 Seçilen Tarih: {targetDate:dd.MM.yyyy}\n\n" +
                             $"Bu gün için resmî GÜN SONU KAPANIŞI yapılacak ve Z RAPORU oluşturulacak:\n\n" +
                             $"• Toplam Satış Adedi: {summary.SalesCount}\n" +
                             $"• Toplam Ciro: ₺{summary.TotalRevenue:N2}\n" +
                             $"  - Nakit: ₺{summary.TotalCash:N2}\n" +
                             $"  - Kredi Kartı: ₺{summary.TotalCreditCard:N2}\n" +
                             $"  - Veresiye: ₺{summary.TotalOnAccount:N2}\n" +
                             $"• Kasa Açılış Tutarı: ₺{cashSummary.OpeningCash:N2}\n" +
                             $"• Gün Sonu Kasa Bakiyesi: ₺{cashSummary.CurrentBalance:N2}\n\n" +
                             $"Gün sonunu onaylayıp resmi kapanış kaydını oluşturmak istiyor musunuz?";

            var ask = MessageBox.Show(confirmMsg, "Gün Sonu Kapanış Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;

            bool success = await _closureService.SaveClosureAsync(summary);
            if (success)
            {
                MessageBox.Show(
                    $"{targetDate:dd.MM.yyyy} tarihli Gün Sonu başarıyla tamamlandı ve Z Raporu arşive kaydedildi.\n\n" +
                    $"📡 Değişen veriler (satışlar, ciro ve kasa hareketleri) merkeze aktarılmak üzere otomatik olarak arşivlendi/gönderildi!\nKasa oturumu kapatıldı.", 
                    "Gün Sonu & Otomatik Senkronizasyon Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);

                _activeCategory = "DAILY";
                _selectedReport = "Z";
                UpdateCategoryAndCardsUI();
                await RefreshPreviewDataAsync();
                await LoadClosuresAsync();

                var askPdf = MessageBox.Show("Oluşturulan Z Raporu resmî PDF dökümünü şimdi açmak ister misiniz?", "PDF Görüntüle", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (askPdf == MessageBoxResult.Yes)
                {
                    var zData = await _dataService.GetZReportDataAsync(targetDate);
                    if (zData != null)
                    {
                        var filePath = _pdfService.GenerateZReportPdf(zData);
                        PdfReportService.OpenPdfFile(filePath);
                    }
                }
            }
            else
            {
                MessageBox.Show("Gün sonu kaydedilirken bir sorun oluştu.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gün sonu işlemi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion
}