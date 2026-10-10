using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class WarehouseView : Page
{
    private readonly WarehouseService _warehouseService;
    private readonly ProductService _productService;
    private List<Warehouse> _warehouses = new();
    private List<WarehouseStock> _stocks = new();

    public WarehouseView()
    {
        InitializeComponent();
        _warehouseService = new WarehouseService();
        _productService = new ProductService();

        // Önbellekteki hazır depoları ve stokları anında bağla
        if (AppDataCache.IsLoaded && AppDataCache.Warehouses.Count > 0)
        {
            _warehouses = new List<Warehouse>(AppDataCache.Warehouses);
            WarehousesGrid.ItemsSource = _warehouses;

            var selectorList = new List<Warehouse> { new Warehouse { Id = 0, Name = "Tüm Depolar" } };
            selectorList.AddRange(_warehouses);
            CmbWarehouseSelector.ItemsSource = selectorList;
            CmbWarehouseSelector.SelectedIndex = 0;

            if (AppDataCache.WarehouseStocks.Count > 0)
            {
                _stocks = new List<WarehouseStock>(AppDataCache.WarehouseStocks);
                StocksGrid.ItemsSource = _stocks;
                TxtTotalWarehouses.Text = _warehouses.Count.ToString();
                TxtTotalProductTypes.Text = _stocks.Select(s => s.ProductId).Distinct().Count().ToString();
                TxtTotalStockQuantity.Text = $"{_stocks.Sum(s => s.Quantity):N0} Adet";
                TxtTotalStockValue.Text = $"₺{_stocks.Sum(s => s.TotalValue):N2}";
            }
        }

        Loaded += WarehouseView_Loaded;
    }

    private async void WarehouseView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _warehouses = await _warehouseService.GetAllWarehousesAsync();
        WarehousesGrid.ItemsSource = _warehouses;

        var selectorList = new List<Warehouse>
        {
            new Warehouse { Id = 0, Name = "Tüm Depolar" }
        };
        selectorList.AddRange(_warehouses);

        int prevSelectedId = 0;
        if (CmbWarehouseSelector.SelectedItem is Warehouse sel)
        {
            prevSelectedId = sel.Id;
        }

        CmbWarehouseSelector.ItemsSource = selectorList;

        var matching = selectorList.FirstOrDefault(w => w.Id == prevSelectedId);
        CmbWarehouseSelector.SelectedItem = matching ?? selectorList[0];

        await RefreshStocksAsync();
    }

    private async Task RefreshStocksAsync()
    {
        int selectedWhId = 0;
        if (CmbWarehouseSelector.SelectedItem is Warehouse sel)
        {
            selectedWhId = sel.Id;
        }

        var search = TxtSearch.Text.Trim();
        _stocks = await _warehouseService.GetWarehouseStocksAsync(selectedWhId > 0 ? selectedWhId : null, search);
        StocksGrid.ItemsSource = _stocks;

        UpdateSummaryCards();
    }

    private void UpdateSummaryCards()
    {
        TxtTotalWarehouses.Text = _warehouses.Count.ToString();

        // Benzersiz ürün çeşidi
        var uniqueProducts = _stocks.Select(s => s.ProductId).Distinct().Count();
        TxtTotalProductTypes.Text = uniqueProducts.ToString();

        // Toplam stok adedi
        var totalQty = _stocks.Sum(s => s.Quantity);
        TxtTotalStockQuantity.Text = $"{totalQty:N0} Adet";

        // Toplam stok maliyeti
        var totalValue = _stocks.Sum(s => s.TotalValue);
        TxtTotalStockValue.Text = $"₺{totalValue:N2}";
    }

    private async void CmbWarehouseSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await RefreshStocksAsync();
    }

    private async void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        await RefreshStocksAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async void CreateWarehouse_Click(object sender, RoutedEventArgs e)
    {
        var win = new WarehouseEditWindow(null, _warehouseService) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await LoadDataAsync();
        }
    }

    private async void EditWarehouse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Warehouse wh)
        {
            var win = new WarehouseEditWindow(wh, _warehouseService) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                await LoadDataAsync();
            }
        }
    }

    private async void DeleteWarehouse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Warehouse wh)
        {
            if (wh.IsDefault)
            {
                MessageBox.Show("Varsayılan ana depo silinemez!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var res = MessageBox.Show($"'{wh.Name}' isimli depoyu silmek / pasife almak istiyor musunuz?", "Depo Sil", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                await _warehouseService.DeleteWarehouseAsync(wh.Id);
                await LoadDataAsync();
            }
        }
    }

    private async void OpenTransfer_Click(object sender, RoutedEventArgs e)
    {
        var win = new WarehouseTransferWindow(_warehouseService, _productService) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await RefreshStocksAsync();
        }
    }

    private async void ManualStockIn_Click(object sender, RoutedEventArgs e)
    {
        int? whId = null;
        if (CmbWarehouseSelector.SelectedItem is Warehouse sel && sel.Id > 0)
        {
            whId = sel.Id;
        }

        var win = new StockAdjustmentDialog(isStockIn: true, warehouseId: whId) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await RefreshStocksAsync();
        }
    }

    private async void ManualStockOut_Click(object sender, RoutedEventArgs e)
    {
        int? whId = null;
        if (CmbWarehouseSelector.SelectedItem is Warehouse sel && sel.Id > 0)
        {
            whId = sel.Id;
        }

        var win = new StockAdjustmentDialog(isStockIn: false, warehouseId: whId) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await RefreshStocksAsync();
        }
    }

    private async void RowStockIn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is WarehouseStock stock)
        {
            var win = new StockAdjustmentDialog(isStockIn: true, productId: stock.ProductId, warehouseId: stock.WarehouseId) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                await RefreshStocksAsync();
            }
        }
    }

    private async void RowStockOut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is WarehouseStock stock)
        {
            var win = new StockAdjustmentDialog(isStockIn: false, productId: stock.ProductId, warehouseId: stock.WarehouseId) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                await RefreshStocksAsync();
            }
        }
    }

    private void OpenStockMovements_Click(object sender, RoutedEventArgs e)
    {
        var win = new StockMovementsWindow(_warehouseService) { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }
}
