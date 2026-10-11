using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class InvoicesView : Page
{
    private readonly InvoiceService _invoiceService;
    private readonly WarehouseService _warehouseService;
    private readonly ProductService _productService;
    private List<Invoice> _invoices = new();

    public InvoicesView()
    {
        InitializeComponent();
        _warehouseService = new WarehouseService();
        _productService = new ProductService();
        _invoiceService = new InvoiceService(_warehouseService, _productService);

        Loaded += InvoicesView_Loaded;
    }

    private async void InvoicesView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadInvoicesAsync();
    }

    private async Task LoadInvoicesAsync()
    {
        string? type = null;
        if (CmbTypeFilter.SelectedItem is ComboBoxItem item)
        {
            type = item.Content.ToString();
        }

        var search = TxtSearch.Text.Trim();
        var start = DpStart.SelectedDate;
        var end = DpEnd.SelectedDate;

        _invoices = await _invoiceService.GetInvoicesFilteredAsync(type, start, end, search);
        InvoicesGrid.ItemsSource = _invoices;

        UpdateSummaryCards();
    }

    private void UpdateSummaryCards()
    {
        TxtTotalInvoices.Text = _invoices.Count.ToString();

        var purchases = _invoices.Where(i => i.Type == "Alış Faturası").ToList();
        var sales = _invoices.Where(i => i.Type == "Satış Faturası").ToList();

        var totalPurchaseAmount = purchases.Sum(i => i.GrandTotal);
        var totalSalesAmount = sales.Sum(i => i.GrandTotal);
        var totalItemsReceived = purchases.Sum(i => i.TotalQuantity);

        TxtTotalPurchases.Text = $"₺{totalPurchaseAmount:N2}";
        TxtTotalSalesInvoices.Text = $"₺{totalSalesAmount:N2}";
        TxtTotalItemsReceived.Text = $"{totalItemsReceived:N0} Adet";
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await LoadInvoicesAsync();
        }
    }

    private async void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await LoadInvoicesAsync();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadInvoicesAsync();
    }

    private bool _isProcessing = false;

    private async void CreateInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        _isProcessing = true;
        try
        {
            var win = new InvoiceEditWindow(_invoiceService, _warehouseService, _productService) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                await LoadInvoicesAsync();
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void ViewDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Invoice invoice)
        {
            var win = new InvoiceDetailWindow(invoice) { Owner = Window.GetWindow(this) };
            win.ShowDialog();
        }
    }

    private async void DeleteInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        if (sender is Button btn && btn.DataContext is Invoice invoice)
        {
            var confirm = MessageBox.Show(
                $"Bu faturayı silmek istediğinize emin misiniz?\n\n" +
                $"• Fatura No: {invoice.InvoiceNo}\n" +
                $"• Cari/Tedarikçi: {invoice.SupplierOrCustomer}\n" +
                $"• Tarih: {invoice.FormattedDate}\n" +
                $"• Tutar: {invoice.FormattedGrandTotal}\n" +
                $"• Ödeme: {invoice.PaymentSummaryText}\n\n" +
                $"⚠️ ÖNEMLİ: Bu fatura silindiğinde faturadaki ürünler depodan düşülecek (veya iade edilecek) ve yapılmış olan ödeme hareketleri geri alınacaktır.",
                "Faturayı Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.Yes)
            {
                _isProcessing = true;
                btn.IsEnabled = false;
                try
                {
                    await _invoiceService.DeleteInvoiceAsync(invoice.Id);
                    await LoadInvoicesAsync();
                }
                finally
                {
                    _isProcessing = false;
                    btn.IsEnabled = true;
                }
            }
        }
    }
}
