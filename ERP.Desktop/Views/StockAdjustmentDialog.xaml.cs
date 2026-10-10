using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class StockAdjustmentDialog : Window
{
    private readonly WarehouseService _warehouseService;
    private readonly ProductService _productService;
    private List<Product> _products = new();
    private List<Warehouse> _warehouses = new();
    private readonly int? _preselectedProductId;
    private readonly int? _preselectedWarehouseId;
    private readonly bool _initialIsStockIn;

    public StockAdjustmentDialog(bool isStockIn = true, int? productId = null, int? warehouseId = null)
    {
        InitializeComponent();
        _warehouseService = new WarehouseService();
        _productService = new ProductService();
        _initialIsStockIn = isStockIn;
        _preselectedProductId = productId;
        _preselectedWarehouseId = warehouseId;

        Loaded += async (s, e) => await LoadFormDataAsync();
    }

    private async Task LoadFormDataAsync()
    {
        try
        {
            if (_initialIsStockIn)
            {
                RbStockIn.IsChecked = true;
            }
            else
            {
                RbStockOut.IsChecked = true;
            }
            UpdateModeUI();

            _warehouses = await _warehouseService.GetAllWarehousesAsync();
            CmbWarehouse.ItemsSource = _warehouses;
            if (_warehouses.Count > 0)
            {
                CmbWarehouse.SelectedValue = _preselectedWarehouseId ?? _warehouses.First().Id;
            }

            _products = await _productService.GetAllProductsAsync();
            CmbProducts.ItemsSource = _products;
            if (_products.Count > 0)
            {
                CmbProducts.SelectedValue = _preselectedProductId ?? _products.First().Id;
                UpdateCurrentStockText();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Veriler yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OperationType_Changed(object sender, RoutedEventArgs e)
    {
        UpdateModeUI();
    }

    private void UpdateModeUI()
    {
        if (TxtTitle == null || BtnSave == null) return;

        bool isStockIn = RbStockIn.IsChecked == true;
        if (isStockIn)
        {
            TxtTitle.Text = "📥 Depo Stok Girişi (+)";
            TxtSubTitle.Text = "Seçilen depoya manuel olarak ürün stoğu ekleyin";
            BtnSave.Content = "💾 Stok Girişini Kaydet";
            BtnSave.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
            TxtDescription.Text = "Manuel Stok Girişi (Tedarik / Sayım)";
        }
        else
        {
            TxtTitle.Text = "📤 Depo Stok Çıkışı (-)";
            TxtSubTitle.Text = "Seçilen depodan manuel olarak ürün stoğu düşün";
            BtnSave.Content = "💾 Stok Çıkışını Kaydet";
            BtnSave.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            TxtDescription.Text = "Manuel Stok Çıkışı (Zayi / Hasar / İade)";
        }
    }

    private void CmbProducts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCurrentStockText();
    }

    private void UpdateCurrentStockText()
    {
        if (CmbProducts.SelectedItem is Product prod)
        {
            TxtCurrentStock.Text = $"Mevcut Stok: {prod.Stock:N0} Adet (Satış: ₺{prod.SalePrice:N2})";
        }
        else
        {
            TxtCurrentStock.Text = "Mevcut Stok: - Adet";
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CmbWarehouse.SelectedValue is not int warehouseId)
        {
            MessageBox.Show("Lütfen hedef depoyu seçin.", "Depo Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CmbProducts.SelectedItem is not Product selectedProduct)
        {
            MessageBox.Show("Lütfen ürün seçin.", "Ürün Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!decimal.TryParse(TxtQuantity.Text?.Trim(), out decimal qty) || qty <= 0)
        {
            MessageBox.Show("Lütfen geçerli bir adet girin (0'dan büyük olmalıdır).", "Geçersiz Adet", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtQuantity.Focus();
            return;
        }

        bool isStockIn = RbStockIn.IsChecked == true;
        string desc = string.IsNullOrWhiteSpace(TxtDescription.Text) ? "Manuel Stok Hareketi" : TxtDescription.Text.Trim();

        try
        {
            BtnSave.IsEnabled = false;

            if (isStockIn)
            {
                await _warehouseService.AddStockAsync(
                    warehouseId,
                    selectedProduct.Id,
                    qty,
                    "Manuel Giriş",
                    desc,
                    "Stok Girişi"
                );

                MessageBox.Show($"{selectedProduct.Name} ürününe {qty:N0} adet stok başarıyla eklendi.", "Stok Girişi Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                await _warehouseService.DecreaseStockAsync(
                    warehouseId,
                    selectedProduct.Id,
                    qty,
                    "Manuel Çıkış",
                    desc,
                    "Stok Çıkışı"
                );

                MessageBox.Show($"{selectedProduct.Name} ürününden {qty:N0} adet stok başarıyla düşüldü.", "Stok Çıkışı Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Stok hareketi işlenirken hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnSave.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
