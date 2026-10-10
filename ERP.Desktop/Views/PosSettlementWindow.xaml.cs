using System.Globalization;
using System.Windows;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class PosSettlementWindow : Window
{
    private readonly PosTransaction _posTransaction;
    private readonly BankService _bankService;
    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    public PosSettlementWindow(PosTransaction posTransaction, BankService bankService)
    {
        InitializeComponent();
        _posTransaction = posTransaction;
        _bankService = bankService;

        TxtSaleDate.Text = posTransaction.FormattedSaleDate;
        TxtGrossAmount.Text = posTransaction.FormattedGross;
        TxtCommission.Text = $"-{posTransaction.FormattedCommission} ({posTransaction.FormattedCommissionRate})";
        TxtExpectedNet.Text = posTransaction.FormattedNet;

        TxtActualPaid.Text = NumberParser.FormatForEditing(posTransaction.NetAmount);
        DpPaymentDate.SelectedDate = DateTime.Today;
        TxtDescription.Text = $"POS Tahsilatı - Satış/Fiş #{posTransaction.SaleId} (Brüt: {posTransaction.FormattedGross}, Komisyon: {posTransaction.FormattedCommission})";

        Loaded += async (_, _) =>
        {
            var accounts = await _bankService.GetAllAccountsAsync();
            CmbAccounts.ItemsSource = accounts;
            if (accounts.Count > 0)
            {
                var sel = accounts.FirstOrDefault(a => a.Id == posTransaction.BankAccountId)
                          ?? accounts.FirstOrDefault(a => a.AccountName.Contains("POS", StringComparison.OrdinalIgnoreCase))
                          ?? accounts.FirstOrDefault(a => a.IsDefault)
                          ?? accounts[0];
                CmbAccounts.SelectedItem = sel;
            }
            TxtActualPaid.Focus();
        };
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!NumberParser.TryParseDecimal(TxtActualPaid.Text, out var actualPaid) || actualPaid <= 0)
        {
            MessageBox.Show("Lütfen hesaba yatan geçerli bir net tutar giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtActualPaid.Focus();
            return;
        }

        if (CmbAccounts.SelectedItem is not BankAccount selectedAccount)
        {
            MessageBox.Show("Lütfen tahsilatın yattığı banka hesabını seçiniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var paymentDate = DpPaymentDate.SelectedDate ?? DateTime.Today;
        var desc = TxtDescription.Text.Trim();

        try
        {
            await _bankService.SettlePosTransactionAsync(
                _posTransaction.Id,
                selectedAccount.Id,
                actualPaid,
                paymentDate.Add(DateTime.Now.TimeOfDay),
                desc);

            MessageBox.Show(
                $"POS tahsilatı başarıyla işlendi!\n\n" +
                $"Banka: {selectedAccount.BankName} - {selectedAccount.AccountName}\n" +
                $"Yatan Tutar: ₺{actualPaid:N2}\n" +
                $"Durum: Ödendi",
                "Başarılı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Tahsilat işlenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
