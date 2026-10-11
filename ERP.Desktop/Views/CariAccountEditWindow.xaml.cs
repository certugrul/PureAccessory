using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class CariAccountEditWindow : Window
{
    private readonly CariAccount? _existingAccount;
    private readonly CariService _cariService;

    public CariAccountEditWindow(CariAccount? account, CariService cariService)
    {
        InitializeComponent();
        _existingAccount = account;
        _cariService = cariService;

        if (_existingAccount != null)
        {
            TxtTitle.Text = "✏️ Cari Kartı Düzenle";
            TxtName.Text = _existingAccount.Name;
            TxtPhone.Text = _existingAccount.Phone ?? "";
            TxtTaxNo.Text = _existingAccount.TaxNo ?? "";
            TxtAddress.Text = _existingAccount.Address ?? "";

            foreach (ComboBoxItem item in CmbType.Items)
            {
                if (item.Content.ToString() == _existingAccount.Type)
                {
                    CmbType.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private bool _isSaving = false;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving) return;
        var btn = sender as Button;

        var name = TxtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Lütfen cari ünvanını / adını giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        var type = (CmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Müşteri";

        var acc = _existingAccount ?? new CariAccount();
        acc.Name = name;
        acc.Type = type;
        acc.Phone = TxtPhone.Text.Trim();
        acc.TaxNo = TxtTaxNo.Text.Trim();
        acc.Address = TxtAddress.Text.Trim();

        _isSaving = true;
        if (btn != null)
        {
            btn.IsEnabled = false;
            btn.Content = "⏳ Kaydediliyor...";
        }

        try
        {
            await _cariService.SaveOrUpdateAccountAsync(acc);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isSaving = false;
            if (btn != null)
            {
                btn.IsEnabled = true;
                btn.Content = "💾 Kaydet";
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
