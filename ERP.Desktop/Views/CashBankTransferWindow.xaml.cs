using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class CashBankTransferWindow : Window
{
    private readonly BankService _bankService;
    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    public CashBankTransferWindow(BankService bankService, int? preselectedAccountId = null)
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

        bool isCashToBank = (CmbDirection.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "CashToBank";
        var date = DpDate.SelectedDate ?? DateTime.Today;
        var desc = TxtDescription.Text.Trim();

        try
        {
            if (isCashToBank)
            {
                var cashService = new CashService();
                decimal currentCash = await cashService.GetTotalCashBalanceAsync();
                if (amount > currentCash)
                {
                    var conf = MessageBox.Show(
                        $"DİKKAT: Kasadaki nakit tutar yetersiz!\n\n" +
                        $"• Kasadaki Mevcut Nakit : ₺{currentCash:N2}\n" +
                        $"• Yatırılmak İstenen     : ₺{amount:N2}\n" +
                        $"• Kasa Açığı             : -₺{(amount - currentCash):N2}\n\n" +
                        "Kasadaki nakit tutarı bankaya aktarılacak tutarı karşılamıyor. Yine de işleme devam edilsin mi?",
                        "Kasa Bakiyesi Yetersiz",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (conf != MessageBoxResult.Yes) return;
                }

                // Kasadan Bankaya: Banka +, Kasa -
                await _bankService.TransferCashToBankAsync(selectedAccount.Id, amount, date, desc);
            }
            else
            {
                // Bankadan Kasaya: Banka -, Kasa +
                if (selectedAccount.CurrentBalance < amount)
                {
                    var conf = MessageBox.Show(
                        $"Banka hesabında ({selectedAccount.BankName}) çekilmek istenen tutar kadar bakiye bulunmuyor!\n\n" +
                        $"Mevcut Bakiye: ₺{selectedAccount.CurrentBalance:N2}\n" +
                        $"Çekilecek Tutar: ₺{amount:N2}\n\n" +
                        "İşleme devam edilsin mi?",
                        "Bakiye Uyarısı",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (conf != MessageBoxResult.Yes) return;
                }

                await _bankService.TransferBankToCashAsync(selectedAccount.Id, amount, date, desc);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Transfer gerçekleştirilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
