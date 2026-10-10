using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class ProductsView : Page
{
    private readonly ProductService _productService;
    public ObservableCollection<Product> Products { get; } = new();

    public ProductsView()
    {
        InitializeComponent();
        _productService = new ProductService();
        ProductGrid.ItemsSource = Products;

        // Önbellekteki hazır veriyi anında doldur (bekleme veya boş ekran olmasın)
        if (AppDataCache.IsLoaded && AppDataCache.Products.Count > 0)
        {
            foreach (var p in AppDataCache.Products)
            {
                Products.Add(p);
            }
            var totalUnits = Products.Sum(p => p.Stock);
            ProductCountText.Text = $"{Products.Count} Çeşit Ürün ({totalUnits:N0} Adet Stok)";
        }

        Loaded += ProductsView_Loaded;
    }

    private async void ProductsView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadProductsAsync();
    }

    private async Task LoadProductsAsync()
    {
        var list = await _productService.GetAllProductsAsync();
        Products.Clear();
        foreach (var p in list)
        {
            Products.Add(p);
        }

        if (Products.Count == 0)
        {
            ProductCountText.Text = "0 Ürün (Veritabanı bilgisine ulaşılamadı)";
            MessageBox.Show("Ürün verilerine veya veritabanına ulaşılamadı!\nLütfen sunucu bağlantınızı kontrol ediniz.", "Bilgiye Ulaşılamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            var totalUnits = Products.Sum(p => p.Stock);
            ProductCountText.Text = $"{Products.Count} Çeşit Ürün ({totalUnits:N0} Adet Stok)";
        }
        SearchBox.Clear();
    }

    private void AddProduct_Click(object sender, RoutedEventArgs e)
    {
        var parentWindow = Window.GetWindow(this);
        var dialog = new ProductEditWindow(null, _productService)
        {
            Owner = parentWindow
        };

        if (dialog.ShowDialog() == true)
        {
            _ = LoadProductsAsync();
            MessageBox.Show("Yeni ürün başarıyla eklendi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenBarcodeCenter_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var parentWindow = Window.GetWindow(this);
            var win = new BarcodePrintWindow(null, _productService)
            {
                Owner = parentWindow
            };
            win.ShowDialog();
            _ = LoadProductsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Barkod baskı merkezi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PrintProductLabel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Product product)
        {
            try
            {
                var parentWindow = Window.GetWindow(this);
                var win = new BarcodePrintWindow(product.Id, _productService)
                {
                    Owner = parentWindow
                };
                win.ShowDialog();
                _ = LoadProductsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Etiket penceresi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void EditProduct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Product product)
        {
            var parentWindow = Window.GetWindow(this);
            var dialog = new ProductEditWindow(product, _productService)
            {
                Owner = parentWindow
            };

            if (dialog.ShowDialog() == true)
            {
                _ = LoadProductsAsync();
                MessageBox.Show("Ürün bilgileri başarıyla güncellendi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void DeleteProduct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Product product)
        {
            var res = MessageBox.Show(
                $"'{product.Name}' ({product.Code}) adlı ürünü silmek istediğinize emin misiniz?",
                "Ürün Silme Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                var success = await _productService.DeleteProductAsync(product.Id);
                if (success)
                {
                    await LoadProductsAsync();
                    MessageBox.Show("Ürün başarıyla silindi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Ürün silinirken bir hata oluştu.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        FilterProducts();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FilterProducts();
        }
    }

    private void FilterProducts()
    {
        var q = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(q))
        {
            ProductGrid.ItemsSource = Products;
            var totalUnits = Products.Sum(p => p.Stock);
            ProductCountText.Text = $"{Products.Count} Çeşit Ürün ({totalUnits:N0} Adet Stok)";
            return;
        }

        var filtered = Products.Where(x =>
            x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(x.Barcode) && x.Barcode.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(x.QrCode) && x.QrCode.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
            x.Code.Contains(q, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        ProductGrid.ItemsSource = new ObservableCollection<Product>(filtered);
        var filteredUnits = filtered.Sum(p => p.Stock);
        ProductCountText.Text = $"{filtered.Count} Ürün Bulundu ({filteredUnits:N0} Adet Stok)";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        ProductGrid.ItemsSource = Products;
        var totalUnits = Products.Sum(p => p.Stock);
        ProductCountText.Text = $"{Products.Count} Çeşit Ürün ({totalUnits:N0} Adet Stok)";
    }
}
