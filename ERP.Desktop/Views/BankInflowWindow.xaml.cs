using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class BankInflowWindow : Window
{
    private readonly BankService _bankService;
    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    public BankInflowWindow(BankService bankService, int? preselectedAccountId = null)
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

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
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

        var sourceType = (CmbSourceType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Dışarıdan Para Girişi";
        var paymentMethod = (CmbPaymentMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Havale";
        var date = DpDate.SelectedDate ?? DateTime.Today;
        var desc = TxtDescription.Text.Trim();
        if (string.IsNullOrWhiteSpace(desc))
        {
            desc = $"{sourceType} ({paymentMethod})";
        }

        var cashService = new CashService();

        // Eğer Nakit Yatırma seçildiyse: Kasadaki nakit kontrolü ve uyarısı
        if (paymentMethod == "Nakit Yatırma")
        {
            decimal currentCash = await cashService.GetTotalCashBalanceAsync();
            if (amount > currentCash)
            {
                var conf = MessageBox.Show(
                    $"DİKKAT: Kasadaki nakit tutar yetersiz!\n\n" +
                    $"• Kasadaki Mevcut Nakit : ₺{currentCash:N2}\n" +
                    $"• Yatırılmak İstenen     : ₺{amount:N2}\n" +
                    $"• Kasa Açığı             : -₺{(amount - currentCash):N2}\n\n" +
                    "Kasadaki nakit tutarı bankaya yatırılacak tutarı karşılamıyor. Yine de işleme devam edilsin mi?",
                    "Kasa Bakiyesi Yetersiz",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (conf != MessageBoxResult.Yes) return;
            }
        }

        try
        {
            // 1. Bankaya para girişi
            await _bankService.AddTransactionAsync(
                selectedAccount.Id,
                sourceType,
                "Giriş",
                amount,
                desc,
                paymentMethod,
                null,
                "MANUEL",
                date.Add(DateTime.Now.TimeOfDay));

            // 2. Eğer Nakit Yatırma ise kasadan para çıkışı (Kasa -, Banka +)
            if (paymentMethod == "Nakit Yatırma")
            {
                await cashService.AddTransactionAsync(
                    "Bankaya Transfer",
                    amount,
                    $"Bankaya nakit yatırma ({selectedAccount.BankName} - {selectedAccount.AccountName}). {desc}");
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Para girişi kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
