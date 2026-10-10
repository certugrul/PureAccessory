using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class DailyReceiptBreakdownWindow : Window
{
    private readonly SalesService _salesService;
    private readonly PdfReportService _pdfReportService;
    private DateTime _targetDate;
    private DailyReceiptBreakdownData? _data;
    private bool _isLoading = false;

    public DailyReceiptBreakdownWindow(DateTime? initialDate = null)
    {
        InitializeComponent();
        _salesService = new SalesService();
        _pdfReportService = new PdfReportService();

        _targetDate = (initialDate ?? DateTime.Today).Date;
        SelectedDatePicker.SelectedDate = _targetDate;

        Loaded += async (s, e) => await LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            TxtActiveDateHeader.Text = _targetDate.ToString("dd.MM.yyyy");
            if (SelectedDatePicker.SelectedDate?.Date != _targetDate)
            {
                SelectedDatePicker.SelectedDate = _targetDate;
            }

            _data = await _salesService.GetDailyReceiptBreakdownAsync(_targetDate);

            // 1. Özet Kartları Güncelle
            TxtTotalSalesCount.Text = $"{_data.TotalSalesCount} Fiş";
            TxtSalesSubInfo.Text = _data.TotalSalesCount > 0 ? "Tamamlanan satış işlemleri" : "Bu tarihte satış yapılmadı";

            TxtTotalQuantitySold.Text = $"{_data.TotalQuantitySold:N0} Adet";
            TxtQuantitySubInfo.Text = _data.TotalQuantitySold > 0 ? "Toplam ürün satışı adedi" : "0 ürün satıldı";

            TxtDistinctProductCount.Text = $"{_data.TotalDistinctProducts} Çeşit";
            TxtDistinctSubInfo.Text = _data.TotalDistinctProducts > 0 ? "Farklı kalem ürün sayısı" : "0 çeşit";

            TxtTotalRevenue.Text = $"₺{_data.TotalRevenue:N2}";
            TxtCashShare.Text = $"💵 Nakit: ₺{_data.CashRevenue:N2}";
            TxtCardShare.Text = $"💳 Kart: ₺{_data.CreditCardRevenue:N2}";
            TxtOnAccountShare.Text = $"📝 Veresiye: ₺{_data.OnAccountRevenue:N2}";

            // 2. Satılan Ürünler Tablosunu Filtrele ve Bağla
            ApplyProductFilter();

            // 3. Fişler Tablosunu Bağla
            ReceiptsGrid.ItemsSource = _data.Receipts;
            TxtReceiptsCountBadge.Text = $"{_data.Receipts.Count} Fiş";

            if (_data.Receipts.Count > 0)
            {
                ReceiptsGrid.SelectedIndex = 0;
            }
            else
            {
                ClearReceiptDetail();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Veriler yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ApplyProductFilter()
    {
        if (_data == null) return;

        var filter = (TxtProductFilter.Text ?? "").Trim().ToLowerInvariant();
        var list = _data.SoldProducts;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            list = list.Where(p => 
                (p.ProductName ?? "").ToLowerInvariant().Contains(filter) ||
                (p.ProductCode ?? "").ToLowerInvariant().Contains(filter)
            ).ToList();
        }

        ProductsGrid.ItemsSource = list;
        TxtProductFilterCount.Text = $"{list.Count} Kalem Listelendi";

        // Tablo Altı Toplamları
        var filteredQty = list.Sum(x => x.TotalQuantity);
        var filteredRev = list.Sum(x => x.TotalRevenue);

        TxtProductsFooterQuantity.Text = $"{filteredQty:N0} Adet";
        TxtProductsFooterRevenue.Text = $"₺{filteredRev:N2}";
    }

    private void ProductFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyProductFilter();
    }

    private void ReceiptsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReceiptsGrid.SelectedItem is DailyReceiptRow selected)
        {
            TxtSelectedReceiptTitle = TxtSelectedReceiptTitle ?? (TextBlock)FindName("TxtSelectedReceiptTitle");
            TxtSelectedReceiptTitle.Text = $"Fiş #{selected.SaleId} ({selected.FormattedTime})";
            TxtSelectedReceiptSub.Text = $"İşlem Tarihi: {selected.FormattedDate} | {selected.ItemCount} Kalem Ürün";
            TxtSelectedReceiptPayment.Text = selected.PaymentTypeBadge;

            ReceiptItemsGrid.ItemsSource = selected.Items;
            TxtReceiptFooterItemsCount.Text = $"{selected.Items.Count} Kalem ({selected.TotalQuantity:N0} Adet Ürün)";
            TxtReceiptFooterTotal.Text = $"₺{selected.GrandTotal:N2}";
        }
        else
        {
            ClearReceiptDetail();
        }
    }

    private void ClearReceiptDetail()
    {
        TxtSelectedReceiptTitle.Text = "Lütfen bir fiş seçin";
        TxtSelectedReceiptSub.Text = "Soldaki listeden fişe tıklayarak kalemlerini görebilirsiniz";
        TxtSelectedReceiptPayment.Text = "-";
        ReceiptItemsGrid.ItemsSource = null;
        TxtReceiptFooterItemsCount.Text = "0 Kalem";
        TxtReceiptFooterTotal.Text = "₺0,00";
    }

    private async void DeleteSelectedReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (ReceiptsGrid.SelectedItem is not DailyReceiptRow selected)
        {
            MessageBox.Show("Lütfen önce listeden silmek istediğiniz fişi seçin.", "Fiş Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmMsg = $"⚠️ FİŞ İPTALİ VE SİLME ONAYI\n\n" +
                         $"• Fiş No       : #{selected.SaleId}\n" +
                         $"• İşlem Saati  : {selected.FormattedTime}\n" +
                         $"• Ödeme Türü   : {selected.PaymentTypeBadge}\n" +
                         $"• Kalem Sayısı : {selected.ItemCount} Kalem ({selected.TotalQuantity:N0} Adet Ürün)\n" +
                         $"• Fiş Tutarı   : ₺{selected.GrandTotal:N2}\n\n" +
                         $"Bu fiş silindiğinde:\n" +
                         $"1. Satılan ürünlerin adetleri depoya geri iade edilecek ve stoklar artırılacak.\n" +
                         $"2. Kasa ve POS hareketleri iptal edilecek.\n" +
                         $"3. Fiş kaydı kalıcı olarak kaldırılacak.\n\n" +
                         $"Bu fişi silmek ve satışı iptal etmek istediğinize emin misiniz?";

        var result = MessageBox.Show(confirmMsg, "Fişi Sil / İptal Et", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            BtnDeleteSelectedReceipt.IsEnabled = false;
            bool success = await _salesService.DeleteSaleAsync(selected.SaleId);
            if (success)
            {
                MessageBox.Show($"#{selected.SaleId} numaralı fiş başarıyla iptal edildi ve silindi.\nStoklar depoya iade edildi.", 
                                "Fiş Silindi", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadDataAsync();
            }
            else
            {
                MessageBox.Show("Fiş silinirken bir sorun oluştu.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fiş silinemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnDeleteSelectedReceipt.IsEnabled = true;
        }
    }

    private async void PrevDay_Click(object sender, RoutedEventArgs e)
    {
        _targetDate = _targetDate.AddDays(-1);
        SelectedDatePicker.SelectedDate = _targetDate;
        await LoadDataAsync();
    }

    private async void NextDay_Click(object sender, RoutedEventArgs e)
    {
        _targetDate = _targetDate.AddDays(1);
        SelectedDatePicker.SelectedDate = _targetDate;
        await LoadDataAsync();
    }

    private async void Today_Click(object sender, RoutedEventArgs e)
    {
        _targetDate = DateTime.Today;
        SelectedDatePicker.SelectedDate = _targetDate;
        await LoadDataAsync();
    }

    private async void SelectedDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (SelectedDatePicker.SelectedDate.HasValue && SelectedDatePicker.SelectedDate.Value.Date != _targetDate)
        {
            _targetDate = SelectedDatePicker.SelectedDate.Value.Date;
            await LoadDataAsync();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_data == null || _data.Receipts.Count == 0)
        {
            MessageBox.Show("Bu tarihe ait fiş ve satış kaydı bulunmadığı için rapor oluşturulamadı.", "Kayıt Yok", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var pdfPath = _pdfReportService.GenerateDailyReceiptBreakdownPdf(_data);
            PdfReportService.OpenPdfFile(pdfPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PDF oluşturulurken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.F5)
        {
            await LoadDataAsync();
        }
    }
}
