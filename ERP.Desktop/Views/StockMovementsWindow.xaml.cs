using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class StockMovementsWindow : Window
{
    private readonly WarehouseService _warehouseService;
    private List<StockMovement> _allMovements = new();

    public StockMovementsWindow(WarehouseService? warehouseService = null)
    {
        InitializeComponent();
        _warehouseService = warehouseService ?? new WarehouseService();

        Loaded += StockMovementsWindow_Loaded;
    }

    private async void StockMovementsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var warehouses = await _warehouseService.GetAllWarehousesAsync();
        var filterList = new List<Warehouse>
        {
            new Warehouse { Id = 0, Name = "Tüm Depolar" }
        };
        filterList.AddRange(warehouses);

        CmbWarehouseFilter.ItemsSource = filterList;
        CmbWarehouseFilter.SelectedIndex = 0;

        await LoadMovementsAsync();
    }

    private async Task LoadMovementsAsync()
    {
        _allMovements = await _warehouseService.GetStockMovementsAsync();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        int selectedWarehouseId = 0;
        if (CmbWarehouseFilter.SelectedItem is Warehouse wh)
        {
            selectedWarehouseId = wh.Id;
        }

        var search = TxtSearch.Text.Trim().ToLowerInvariant();

        var query = _allMovements.AsQueryable();

        if (selectedWarehouseId > 0)
        {
            query = query.Where(m => m.WarehouseId == selectedWarehouseId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(m =>
                m.ProductName.ToLowerInvariant().Contains(search) ||
                m.ProductCode.ToLowerInvariant().Contains(search) ||
                m.DocumentNo.ToLowerInvariant().Contains(search) ||
                m.Description.ToLowerInvariant().Contains(search));
        }

        MovementsGrid.ItemsSource = query.ToList();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadMovementsAsync();

    private async void DeleteMovement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is StockMovement m)
        {
            var confirm = MessageBox.Show(
                $"Bu stok hareket kaydını silmek istediğinize emin misiniz?\n\n" +
                $"• Tarih: {m.FormattedDate}\n" +
                $"• Depo: {m.WarehouseName}\n" +
                $"• Ürün: {m.ProductName}\n" +
                $"• Hareket: {m.MovementType} ({m.FormattedQuantity})\n" +
                $"• Belge No: {m.DocumentNo}",
                "Stok Hareketini Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                await _warehouseService.DeleteStockMovementAsync(m.Id);
                await LoadMovementsAsync();
            }
        }
    }

    private async void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        int selectedWarehouseId = 0;
        string whName = "Tüm depoların";
        if (CmbWarehouseFilter.SelectedItem is Warehouse wh && wh.Id > 0)
        {
            selectedWarehouseId = wh.Id;
            whName = $"'{wh.Name}' deposunun";
        }

        var confirm = MessageBox.Show(
            $"{whName} tüm geçmiş stok hareket kayıtlarını kalıcı olarak temizlemek istediğinize emin misiniz?\n\n" +
            "Not: Bu işlem sadece geçmiş denetim hareket loglarını temizler, mevcut stok miktarlarını değiştirmez.",
            "Stok Hareket Geçmişini Temizle",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            await _warehouseService.ClearAllStockMovementsAsync(selectedWarehouseId > 0 ? selectedWarehouseId : null);
            await LoadMovementsAsync();
        }
    }
}
