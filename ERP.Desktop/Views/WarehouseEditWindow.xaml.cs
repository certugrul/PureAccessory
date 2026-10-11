using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class WarehouseEditWindow : Window
{
    private readonly WarehouseService _warehouseService;
    private readonly Warehouse? _existingWarehouse;

    public Warehouse? ResultWarehouse { get; private set; }

    public WarehouseEditWindow(Warehouse? warehouse = null, WarehouseService? warehouseService = null)
    {
        InitializeComponent();
        _warehouseService = warehouseService ?? new WarehouseService();
        _existingWarehouse = warehouse;

        if (_existingWarehouse != null)
        {
            TxtTitle.Text = "✏️ Depoyu Düzenle";
            TxtCode.Text = _existingWarehouse.Code;
            TxtName.Text = _existingWarehouse.Name;
            TxtLocation.Text = _existingWarehouse.Location;
            TxtDescription.Text = _existingWarehouse.Description ?? "";
            ChkIsDefault.IsChecked = _existingWarehouse.IsDefault;
        }
        else
        {
            TxtCode.Text = "DEP-0" + (new Random().Next(2, 9));
        }

        Loaded += (s, e) => TxtName.Focus();
    }

    private bool _isSaving = false;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving) return;
        var btn = sender as Button;

        var name = TxtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Lütfen geçerli bir depo adı giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        var code = TxtCode.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            code = "DEP-01";
        }

        var warehouse = _existingWarehouse ?? new Warehouse();
        warehouse.Code = code;
        warehouse.Name = name;
        warehouse.Location = TxtLocation.Text.Trim();
        warehouse.Description = TxtDescription.Text.Trim();
        warehouse.IsDefault = ChkIsDefault.IsChecked == true;

        _isSaving = true;
        if (btn != null)
        {
            btn.IsEnabled = false;
            btn.Content = "⏳ Kaydediliyor...";
        }

        try
        {
            ResultWarehouse = await _warehouseService.SaveOrUpdateWarehouseAsync(warehouse);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Depo kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isSaving = false;
            if (btn != null)
            {
                btn.IsEnabled = true;
                btn.Content = "💾 Depoyu Kaydet";
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
