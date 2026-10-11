using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class BankOutflowWindow : Window
{
    private readonly BankService _bankService;
    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    public BankOutflowWindow(BankService bankService, int? preselectedAccountId = null)
    {
        InitializeComponent();
        _bankService = bankService;
        DpDate.SelectedDate = DateTime.Today;

        Loaded += async (_, _) =>
        {
            var accounts = await _bankService.GetAllAccountsAsync();
            CmbAccounts.ItemsSource = accounts;
            if (accounts.Count > 0)
            {
                var sel = accounts.FirstOrDefault(a => a.Id == preselectedAccountId)
                          ?? accounts.FirstOrDefault(a => a.IsDefault)
                          ?? accounts[0];
                CmbAccounts.SelectedItem = sel;
            }
            TxtAmount.Focus();
        };
    }

    private bool _isSaving = false;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving) return;
        var btn = sender as Button;

        if (!NumberParser.TryParseDecimal(TxtAmount.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Lütfen geçerli bir pozitif tutar giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAmount.Focus();
            return;
        }

        if (CmbAccounts.SelectedItem is not BankAccount selectedAccount)
        {
            MessageBox.Show("Lütfen bir banka hesabı seçiniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (selectedAccount.CurrentBalance < amount)
        {
            var conf = MessageBox.Show(
                $"Banka hesabında ({selectedAccount.BankName}) yetersiz bakiye bulunmaktadır!\n\n" +
                $"Mevcut Bakiye: ₺{selectedAccount.CurrentBalance:N2}\n" +
                $"Çıkış Tutarı: ₺{amount:N2}\n\n" +
                "Yine de işleme devam edip hesabı eksiye düşürmek istiyor musunuz?",
                "Bakiye Uyarısı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (conf != MessageBoxResult.Yes) return;
        }

        var outflowType = (CmbOutflowType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tedarikçi Ödemesi";
        var paymentMethod = (CmbPaymentMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Havale";
        var date = DpDate.SelectedDate ?? DateTime.Today;
        var desc = TxtDescription.Text.Trim();
        if (string.IsNullOrWhiteSpace(desc))
        {
            desc = $"{outflowType} ({paymentMethod})";
        }

        _isSaving = true;
        if (btn != null)
        {
            btn.IsEnabled = false;
            btn.Content = "⏳ Kaydediliyor...";
        }

        try
        {
            await _bankService.AddTransactionAsync(
                selectedAccount.Id,
                outflowType,
                "Çıkış",
                amount,
                desc,
                paymentMethod,
                null,
                "MANUEL",
                date.Add(DateTime.Now.TimeOfDay));

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Para çıkışı kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isSaving = false;
            if (btn != null)
            {
                btn.IsEnabled = true;
                btn.Content = "💾 Çıkışı Kaydet";
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
