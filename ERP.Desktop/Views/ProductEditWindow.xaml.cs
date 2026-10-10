using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class ProductEditWindow : Window
{
    private readonly ProductService _productService;
    private readonly Product _product;

    public Product ProductResult => _product;

    public ProductEditWindow(Product? existingProduct = null, ProductService? productService = null)
    {
        InitializeComponent();
        _productService = productService ?? new ProductService();

        if (existingProduct != null)
        {
            _product = existingProduct;
            TitleText.Text = "Ürünü Düzenle";
            Title = $"Ürünü Düzenle: {_product.Name}";
            LoadProductToForm();
        }
        else
        {
            _product = new Product();
            TitleText.Text = "Yeni Ürün Ekle";
            Title = "Yeni Ürün Ekle";
            TxtStock.Text = "100";
            TxtPurchasePrice.Text = "0,00";
            TxtSalePrice.Text = "0,00";
        }
    }

    private void LoadProductToForm()
    {
        TxtCode.Text = _product.Code;
        TxtName.Text = _product.Name;
        TxtBarcode.Text = _product.Barcode;
        TxtQrCode.Text = _product.QrCode ?? "";
        TxtPurchasePrice.Text = NumberParser.FormatForEditing(_product.PurchasePrice);
        TxtSalePrice.Text = NumberParser.FormatForEditing(_product.SalePrice);
        TxtStock.Text = _product.Stock.ToString("0.##", CultureInfo.InvariantCulture);

        // KDV seçim
        foreach (ComboBoxItem item in CmbVatRate.Items)
        {
            if (item.Tag?.ToString() == _product.VatRate.ToString("0"))
            {
                item.IsSelected = true;
                break;
            }
        }
    }

    private async void GenerateBarcode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var products = await _productService.GetAllProductsAsync();
            var existingBarcodes = products.Select(p => p.Barcode).Where(b => !string.IsNullOrEmpty(b));
            string newBarcode = BarcodeService.Instance.GenerateEan13Barcode(existingBarcodes);
            TxtBarcode.Text = newBarcode;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Barkod üretilemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PrintBarcode_Click(object sender, RoutedEventArgs e)
    {
        var code = TxtCode.Text.Trim();
        var name = TxtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Barkod yazdırmadan önce lütfen en azından Ürün Kodu ve Ürün Adını girin.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_product.Id == 0)
        {
            var res = MessageBox.Show(
                "Etiket basabilmek için önce ürünün sisteme kaydedilmesi gerekmektedir.\nŞimdi kaydedilsin mi?",
                "Kayıt Gerekli",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            decimal purchasePrice = NumberParser.ParseDecimalSafe(TxtPurchasePrice.Text);
            decimal salePrice = NumberParser.ParseDecimalSafe(TxtSalePrice.Text);
            decimal stock = NumberParser.ParseDecimalSafe(TxtStock.Text);

            _product.Code = code;
            _product.Name = name;
            _product.Barcode = TxtBarcode.Text.Trim();
            _product.QrCode = string.IsNullOrWhiteSpace(TxtQrCode.Text) ? $"QR-{code.ToUpper()}" : TxtQrCode.Text.Trim();
            _product.PurchasePrice = purchasePrice;
            _product.SalePrice = salePrice;
            _product.Stock = stock;

            await _productService.SaveOrUpdateProductAsync(_product);
        }

        try
        {
            var win = new BarcodePrintWindow(_product.Id, _productService)
            {
                Owner = Window.GetWindow(this) ?? this
            };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Baskı merkezi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GenerateQr_Click(object sender, RoutedEventArgs e)
    {
        var code = TxtCode.Text.Trim();
        if (!string.IsNullOrEmpty(code))
        {
            TxtQrCode.Text = $"QR-{code.ToUpper(new CultureInfo("tr-TR"))}";
        }
        else
        {
            TxtQrCode.Text = $"QR-UR-{Random.Shared.Next(100, 999)}";
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var code = TxtCode.Text.Trim();
        var name = TxtName.Text.Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show("Lütfen ürün kodunu girin.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtCode.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Lütfen ürün adını girin.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        // Fiyatları parse et
        decimal purchasePrice = NumberParser.ParseDecimalSafe(TxtPurchasePrice.Text);
        decimal salePrice = NumberParser.ParseDecimalSafe(TxtSalePrice.Text);
        decimal stock = NumberParser.ParseDecimalSafe(TxtStock.Text);

        decimal vatRate = 20;
        if (CmbVatRate.SelectedItem is ComboBoxItem selItem && decimal.TryParse(selItem.Tag?.ToString(), out var vat))
        {
            vatRate = vat;
        }

        _product.Code = code;
        _product.Name = name;
        _product.Barcode = TxtBarcode.Text.Trim();
        _product.QrCode = string.IsNullOrWhiteSpace(TxtQrCode.Text) ? $"QR-{code.ToUpper()}" : TxtQrCode.Text.Trim();
        _product.PurchasePrice = purchasePrice;
        _product.SalePrice = salePrice;
        _product.VatRate = vatRate;
        _product.Stock = stock;

        await _productService.SaveOrUpdateProductAsync(_product);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
