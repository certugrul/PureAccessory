using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class WarehouseTransferWindow : Window
{
    private readonly WarehouseService _warehouseService;
    private readonly ProductService _productService;
    private List<Warehouse> _warehouses = new();
    private List<ProductItemViewModel> _products = new();
    private List<WarehouseStock> _stocks = new();

    public class ProductItemViewModel
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string DisplayInfo => $"{Name} ({Code})";
    }

    public WarehouseTransferWindow(WarehouseService? warehouseService = null, ProductService? productService = null)
    {
        InitializeComponent();
        _warehouseService = warehouseService ?? new WarehouseService();
        _productService = productService ?? new ProductService();

        Loaded += WarehouseTransferWindow_Loaded;
    }

    private async void WarehouseTransferWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _warehouses = await _warehouseService.GetAllWarehousesAsync();
        CmbSourceWarehouse.ItemsSource = _warehouses;
        CmbTargetWarehouse.ItemsSource = _warehouses;

        if (_warehouses.Count > 0)
        {
            CmbSourceWarehouse.SelectedIndex = 0;
            if (_warehouses.Count > 1)
            {
                CmbTargetWarehouse.SelectedIndex = 1;
            }
        }

        var prods = await _productService.GetAllProductsAsync();
        _products = prods.Select(p => new ProductItemViewModel
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name
        }).ToList();

        CmbProduct.ItemsSource = _products;
        if (_products.Count > 0)
        {
            CmbProduct.SelectedIndex = 0;
        }

        await RefreshAvailableStockAsync();
    }

    private async void CmbSourceWarehouse_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await RefreshAvailableStockAsync();
    }

    private async void CmbProduct_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await RefreshAvailableStockAsync();
    }

    private async Task RefreshAvailableStockAsync()
    {
        if (CmbSourceWarehouse.SelectedItem is Warehouse src && CmbProduct.SelectedItem is ProductItemViewModel prod)
        {
            _stocks = await _warehouseService.GetWarehouseStocksAsync(src.Id);
            var itemStock = _stocks.FirstOrDefault(s => s.ProductId == prod.Id);
            var qty = itemStock?.Quantity ?? 0;
            TxtSourceAvailableStock.Text = $"Kaynak Depo ({src.Name}) Mevcut Stok: {qty:N0} Adet";
            TxtSourceAvailableStock.Foreground = qty > 0 ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
        }
    }

    private bool _isProcessing = false;

    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        var btn = sender as Button;

        if (CmbSourceWarehouse.SelectedItem is not Warehouse src)
        {
            MessageBox.Show("Lütfen kaynak depoyu seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CmbTargetWarehouse.SelectedItem is not Warehouse tgt)
        {
            MessageBox.Show("Lütfen hedef depoyu seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (src.Id == tgt.Id)
        {
            MessageBox.Show("Kaynak depo ile hedef depo aynı olamaz! Lütfen farklı bir hedef depo seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CmbProduct.SelectedItem is not ProductItemViewModel prod)
        {
            MessageBox.Show("Lütfen transfer edilecek ürünü seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtQuantity.Text, out decimal qty) || qty <= 0)
        {
            MessageBox.Show("Lütfen geçerli bir transfer miktarı girin (0'dan büyük).", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtQuantity.Focus();
            return;
        }

        _isProcessing = true;
        if (btn != null)
        {
            btn.IsEnabled = false;
            btn.Content = "⏳ Transfer Yapılıyor...";
        }

        try
        {
            await _warehouseService.TransferStockAsync(src.Id, tgt.Id, prod.Id, qty, TxtDescription.Text.Trim());
            MessageBox.Show($"Transfer başarıyla gerçekleştirildi.\n\n• Ürün: {prod.Name}\n• Miktar: {qty:N0} Adet\n• {src.Name} ➔ {tgt.Name}", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Transfer sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isProcessing = false;
            if (btn != null)
            {
                btn.IsEnabled = true;
                btn.Content = "💾 Transferi Gerçekleştir";
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
