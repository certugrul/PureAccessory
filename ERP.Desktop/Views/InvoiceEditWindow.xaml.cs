using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class InvoiceEditWindow : Window
{
    private readonly InvoiceService _invoiceService;
    private readonly WarehouseService _warehouseService;
    private readonly ProductService _productService;
    private readonly BankService _bankService;
    private readonly CariService _cariService;

    private List<Warehouse> _warehouses = new();
    private List<Product> _products = new();
    private List<BankAccount> _bankAccounts = new();
    private List<CariAccount> _cariAccounts = new();
    public ObservableCollection<InvoiceItem> Items { get; } = new();
    private bool _isLoaded = false;

    public class ProductOptionViewModel
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Barcode { get; set; } = "";
        public decimal PurchasePrice { get; set; }
        public decimal SalePrice { get; set; }
        public decimal VatRate { get; set; }
        public string DisplayInfo => $"{Name} ({Code}) - Alış: ₺{PurchasePrice:N2}";
    }

    public InvoiceEditWindow(
        InvoiceService? invoiceService = null,
        WarehouseService? warehouseService = null,
        ProductService? productService = null,
        BankService? bankService = null,
        CariService? cariService = null)
    {
        InitializeComponent();
        _invoiceService = invoiceService ?? new InvoiceService();
        _warehouseService = warehouseService ?? new WarehouseService();
        _productService = productService ?? new ProductService();
        _bankService = bankService ?? new BankService();
        _cariService = cariService ?? new CariService();

        InvoiceItemsGrid.ItemsSource = Items;
        DpInvoiceDate.SelectedDate = DateTime.Today;

        Loaded += InvoiceEditWindow_Loaded;
    }

    private async void InvoiceEditWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _warehouses = await _warehouseService.GetAllWarehousesAsync();
        CmbTargetWarehouse.ItemsSource = _warehouses;
        var defaultWh = _warehouses.FirstOrDefault(w => w.IsDefault) ?? _warehouses.FirstOrDefault();
        if (defaultWh != null)
        {
            CmbTargetWarehouse.SelectedItem = defaultWh;
        }

        _products = await _productService.GetAllProductsAsync();
        CmbProductPicker.ItemsSource = _products.Select(p => new ProductOptionViewModel
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            Barcode = p.Barcode,
            PurchasePrice = p.PurchasePrice,
            SalePrice = p.SalePrice,
            VatRate = p.VatRate
        }).ToList();

        if (CmbProductPicker.Items.Count > 0)
        {
            CmbProductPicker.SelectedIndex = 0;
        }

        // Cari hesapları yükle
        _cariAccounts = await _cariService.GetAllAccountsAsync();
        CmbCariAccount.ItemsSource = _cariAccounts;

        // Banka hesaplarını yükle
        _bankAccounts = await _bankService.GetAllAccountsAsync();
        CmbBankAccount.ItemsSource = _bankAccounts;
        if (_bankAccounts.Count > 0)
        {
            var def = _bankAccounts.FirstOrDefault(a => a.IsDefault) ?? _bankAccounts[0];
            CmbBankAccount.SelectedItem = def;
        }

        TxtInvoiceNo.Text = await _invoiceService.GenerateNextInvoiceNumber("Alış Faturası");

        _isLoaded = true;
        UpdatePaymentVisibility();
    }

    private void CmbPaymentStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        UpdatePaymentVisibility();
    }

    private void CmbPaymentChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        UpdatePaymentVisibility();
    }

    private void UpdatePaymentVisibility()
    {
        if (CmbPaymentStatus == null || PnlPaymentChannel == null) return;

        bool isPaid = (CmbPaymentStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Ödendi";

        if (isPaid)
        {
            PnlPaymentChannel.Visibility = Visibility.Visible;
            PnlCurrentAccount.Visibility = Visibility.Collapsed;

            bool isBank = (CmbPaymentChannel.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Banka Hesabı";
            if (isBank)
            {
                PnlBankAccount.Visibility = Visibility.Visible;
                PnlCashInfo.Visibility = Visibility.Collapsed;
            }
            else
            {
                PnlBankAccount.Visibility = Visibility.Collapsed;
                PnlCashInfo.Visibility = Visibility.Visible;
            }
        }
        else // Ödenmedi
        {
            PnlPaymentChannel.Visibility = Visibility.Collapsed;
            PnlBankAccount.Visibility = Visibility.Collapsed;
            PnlCashInfo.Visibility = Visibility.Collapsed;
            PnlCurrentAccount.Visibility = Visibility.Visible;
        }
    }

    private void CmbCariAccount_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbCariAccount.SelectedItem is CariAccount selCari)
        {
            if (!string.IsNullOrWhiteSpace(selCari.TaxNo))
            {
                TxtTaxNo.Text = selCari.TaxNo;
            }
        }
    }

    private async void CmbInvoiceType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbInvoiceType.SelectedItem is ComboBoxItem item && TxtInvoiceNo != null)
        {
            var type = item.Content.ToString() ?? "Alış Faturası";
            TxtInvoiceNo.Text = await _invoiceService.GenerateNextInvoiceNumber(type);

            if (type == "Alış Faturası")
            {
                TxtModalTitle.Text = "📥 Yeni Alış Faturası Girişi";
                LblUnitPrice.Text = "Birim Alış (₺) *";
            }
            else
            {
                TxtModalTitle.Text = "📤 Yeni Satış Faturası Girişi";
                LblUnitPrice.Text = "Birim Satış (₺) *";
            }

            UpdateUnitPriceForSelectedProduct();
        }
    }

    private void CmbProductPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateUnitPriceForSelectedProduct();
    }

    private void UpdateUnitPriceForSelectedProduct()
    {
        if (CmbProductPicker.SelectedItem is ProductOptionViewModel prod)
        {
            var isPurchase = CmbInvoiceType.SelectedIndex == 0;
            var price = isPurchase ? prod.PurchasePrice : prod.SalePrice;
            TxtItemUnitPrice.Text = NumberParser.FormatForEditing(price);

            // KDV oranını ayarla
            foreach (ComboBoxItem item in CmbItemVatRate.Items)
            {
                if (item.Tag?.ToString() == ((int)prod.VatRate).ToString())
                {
                    CmbItemVatRate.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (CmbProductPicker.SelectedItem is not ProductOptionViewModel prod)
        {
            MessageBox.Show("Lütfen bir ürün seçiniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtItemQuantity.Text, out decimal qty) || qty <= 0)
        {
            MessageBox.Show("Lütfen geçerli bir miktar giriniz (0'dan büyük).", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtItemQuantity.Focus();
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtItemUnitPrice.Text, out decimal unitPrice) || unitPrice < 0)
        {
            MessageBox.Show("Lütfen geçerli bir birim fiyat giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtItemUnitPrice.Focus();
            return;
        }

        decimal vatRate = 20;
        if (CmbItemVatRate.SelectedItem is ComboBoxItem vatItem && decimal.TryParse(vatItem.Tag?.ToString(), out decimal vr))
        {
            vatRate = vr;
        }

        // Eğer ürün zaten listede varsa miktarını artır
        var existing = Items.FirstOrDefault(i => i.ProductId == prod.Id);
        if (existing != null)
        {
            existing.Quantity += qty;
            existing.UnitPrice = unitPrice;
            existing.VatRate = vatRate;
            InvoiceItemsGrid.Items.Refresh();
        }
        else
        {
            Items.Add(new InvoiceItem
            {
                ProductId = prod.Id,
                ProductCode = prod.Code,
                ProductName = prod.Name,
                Barcode = prod.Barcode,
                Quantity = qty,
                UnitPrice = unitPrice,
                VatRate = vatRate
            });
        }

        UpdateTotals();
        TxtItemQuantity.Text = "1";
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is InvoiceItem item)
        {
            Items.Remove(item);
            UpdateTotals();
        }
    }

    private void UpdateTotals()
    {
        decimal subTotal = 0;
        decimal vatTotal = 0;
        decimal totalQty = 0;

        foreach (var item in Items)
        {
            subTotal += item.LineTotal;
            vatTotal += item.VatAmount;
            totalQty += item.Quantity;
        }

        TxtSubTotal.Text = $"₺{subTotal:N2}";
        TxtVatTotal.Text = $"₺{vatTotal:N2}";
        TxtGrandTotal.Text = $"₺{(subTotal + vatTotal):N2}";
        TxtItemsSummary.Text = $"{Items.Count} Kalem / {totalQty:N0} Adet Ürün Eklendi";
    }

    private async void SaveInvoice_Click(object sender, RoutedEventArgs e)
    {
        var invoiceNo = TxtInvoiceNo.Text.Trim();
        if (string.IsNullOrWhiteSpace(invoiceNo))
        {
            MessageBox.Show("Lütfen geçerli bir fatura numarası giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtInvoiceNo.Focus();
            return;
        }

        int? cariAccountId = null;
        var selectedCari = CmbCariAccount.SelectedItem as CariAccount;
        var supplier = CmbCariAccount.Text?.Trim() ?? "";

        if (selectedCari != null)
        {
            cariAccountId = selectedCari.Id;
            supplier = selectedCari.Name;
        }

        if (string.IsNullOrWhiteSpace(supplier))
        {
            MessageBox.Show("Lütfen Tedarikçi / Cari Firma ünvanını seçiniz veya yazınız.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            CmbCariAccount.Focus();
            return;
        }

        if (CmbTargetWarehouse.SelectedItem is not Warehouse wh)
        {
            MessageBox.Show("Lütfen giriş yapılacak depoyu seçiniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Items.Count == 0)
        {
            MessageBox.Show("Faturada en az 1 kalem ürün bulunmalıdır. Lütfen yukarıdan ürün ekleyin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var isPurchase = CmbInvoiceType.SelectedIndex == 0;
        var totalQty = Items.Sum(x => x.Quantity);
        decimal grandTotal = Items.Sum(x => x.GrandTotal);

        string paymentStatus = (CmbPaymentStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Ödendi";
        string paymentType = "Nakit";
        int? bankAccountId = null;
        string? bankAccountName = null;
        string? currentAccountName = supplier;
        decimal paidAmount = 0m;
        decimal remainingAmount = 0m;

        if (paymentStatus == "Ödendi")
        {
            paidAmount = grandTotal;
            remainingAmount = 0m;

            string channel = (CmbPaymentChannel.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Nakit";
            if (channel == "Banka Hesabı")
            {
                paymentType = "Banka Hesabı";
                if (CmbBankAccount.SelectedItem is BankAccount selAcc)
                {
                    bankAccountId = selAcc.Id;
                    bankAccountName = $"{selAcc.BankName} - {selAcc.AccountName}";
                }
                else
                {
                    MessageBox.Show("Lütfen ödemenin yapılacağı/tahsil edileceği banka hesabını seçiniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    CmbBankAccount.Focus();
                    return;
                }
            }
            else
            {
                paymentType = "Nakit";
            }
        }
        else
        {
            paymentStatus = "Ödenmedi";
            paymentType = "Açık Hesap (Cari)";
            paidAmount = 0m;
            remainingAmount = grandTotal;
        }

        string paymentLine = paymentStatus == "Ödendi"
            ? (paymentType == "Banka Hesabı" 
                ? $"• Ödeme: Ödendi (💳 {bankAccountName} hesabından {(isPurchase ? "düşülecek" : "giriş yapılacak")})"
                : $"• Ödeme: Ödendi (💵 Nakit Kasa {(isPurchase ? "çıkışı yapılacak" : "girişi yapılacak")})")
            : $"• Ödeme: Ödenmedi (📋 Cari Açık Hesap: {supplier} {(isPurchase ? "alacağına" : "borcuna")} işlenecek)";

        var confirmMsg = isPurchase
            ? $"Bu faturadaki {Items.Count} kalem ({totalQty:N0} adet) ürün '{wh.Name}' deposuna eklenecek ve depo stokları artırılacaktır.\n\n{paymentLine}\n• Toplam Tutar: ₺{grandTotal:N2}\n\nFaturayı onaylayıp depoya giriş yapmak istiyor musunuz?"
            : $"Bu faturadaki {Items.Count} kalem ({totalQty:N0} adet) ürün '{wh.Name}' deposundan düşülecektir.\n\n{paymentLine}\n• Toplam Tutar: ₺{grandTotal:N2}\n\nFaturayı onaylamak istiyor musunuz?";

        var res = MessageBox.Show(confirmMsg, "Fatura ve Depo Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        var invoice = new Invoice
        {
            InvoiceNo = invoiceNo,
            Type = isPurchase ? "Alış Faturası" : "Satış Faturası",
            InvoiceDate = DpInvoiceDate.SelectedDate ?? DateTime.Today,
            SupplierOrCustomer = supplier,
            CariAccountId = cariAccountId,
            PaidAmount = paidAmount,
            RemainingAmount = remainingAmount,
            TaxNo = TxtTaxNo.Text.Trim(),
            WarehouseId = wh.Id,
            WarehouseName = wh.Name,
            PaymentStatus = paymentStatus,
            PaymentType = paymentType,
            BankAccountId = bankAccountId,
            BankAccountName = bankAccountName,
            CurrentAccountName = currentAccountName,
            Notes = TxtNotes.Text.Trim(),
            Items = Items.ToList()
        };

        try
        {
            await _invoiceService.SaveInvoiceAsync(invoice);

            MessageBox.Show(
                $"✅ Fatura başarıyla kaydedildi!\n\n" +
                $"• Fatura No: {invoice.InvoiceNo}\n" +
                $"• Hedef Depo: {wh.Name}\n" +
                $"• Ödeme Durumu: {invoice.PaymentSummaryText}\n" +
                $"• Eklenen Kalem: {Items.Count} Çeşit ({totalQty:N0} Adet)\n" +
                $"• Genel Toplam: {invoice.FormattedGrandTotal}\n\n" +
                $"Ürünler depoya eklenmiş ve finans/stok hareketleri yansıtılmıştır.",
                "Fatura Kaydedildi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fatura kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
