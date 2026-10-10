using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class BankReconciliationWindow : Window
{
    private readonly BankService _bankService;
    private static readonly CultureInfo TurkishCulture = new("tr-TR");
    private BankAccount? _currentAccount;

    public BankReconciliationWindow(BankService bankService, int? preselectedAccountId = null)
    {
        InitializeComponent();
        _bankService = bankService;

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
        };
    }

    private void CmbAccounts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbAccounts.SelectedItem is BankAccount acc)
        {
            _currentAccount = acc;
            TxtSystemBalance.Text = acc.FormattedCurrentBalance;
            TxtActualBalance.Text = NumberParser.FormatForEditing(acc.ActualBalance > 0 ? acc.ActualBalance : acc.CurrentBalance);

            UpdateDifference();
        }
    }

    private void TxtActualBalance_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDifference();
    }

    private void UpdateDifference()
    {
        if (_currentAccount == null) return;

        NumberParser.TryParseDecimal(TxtActualBalance.Text, out var actual);
        var diff = actual - _currentAccount.CurrentBalance;

        TxtDiffAmount.Text = (diff > 0 ? "+" : "") + $"₺{diff:N2}";

        if (Math.Abs(diff) < 0.01m)
        {
            BrdDiff.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
            BrdDiff.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
            TxtDiffAmount.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
            TxtDiffAlert.Text = "🟢 Tebrikler! ERP sistem bakiyesi ile gerçek banka bakiyesi birebir mutabık.";
            TxtDiffAlert.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#047857"));
            ChkCreateAdjustment.Visibility = Visibility.Collapsed;
        }
        else
        {
            BrdDiff.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
            BrdDiff.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FECACA"));
            TxtDiffAmount.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            TxtDiffAlert.Text = $"⚠️ Banka bakiyesi ile ERP bakiyesi arasında ₺{Math.Abs(diff):N2} tutarında fark bulunmaktadır.";
            TxtDiffAlert.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
            ChkCreateAdjustment.Visibility = Visibility.Visible;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAccount == null) return;

        if (!NumberParser.TryParseDecimal(TxtActualBalance.Text, out var actual))
        {
            MessageBox.Show("Lütfen geçerli bir gerçek bakiye giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtActualBalance.Focus();
            return;
        }

        bool createAdj = ChkCreateAdjustment.IsChecked == true && ChkCreateAdjustment.Visibility == Visibility.Visible;

        try
        {
            await _bankService.ReconcileAccountAsync(_currentAccount.Id, actual, createAdj, "Kullanıcı mutabakat onayı");

            MessageBox.Show(
                "Mutabakat kaydı başarıyla güncellendi!",
                "Bilgi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Mutabakat kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
