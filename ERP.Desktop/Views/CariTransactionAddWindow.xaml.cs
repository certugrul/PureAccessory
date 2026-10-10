using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class CariTransactionAddWindow : Window
{
    private readonly CariService _cariService;
    private readonly BankService _bankService;
    private readonly int? _preselectedAccountId;

    public CariTransactionAddWindow(CariService cariService, int? accountId = null, BankService? bankService = null)
    {
        InitializeComponent();
        _cariService = cariService;
        _bankService = bankService ?? new BankService();
        _preselectedAccountId = accountId;
        DpDate.SelectedDate = DateTime.Today;

        Loaded += CariTransactionAddWindow_Loaded;
    }

    private async void CariTransactionAddWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var accounts = await _cariService.GetAllAccountsAsync();
            CmbAccounts.ItemsSource = accounts;

            if (_preselectedAccountId.HasValue)
            {
                var target = accounts.FirstOrDefault(a => a.Id == _preselectedAccountId.Value);
                if (target != null) CmbAccounts.SelectedItem = target;
            }

            if (CmbAccounts.SelectedItem == null && accounts.Count > 0)
            {
                CmbAccounts.SelectedIndex = 0;
            }

            // Banka hesaplarını yükle
            var bankAccounts = await _bankService.GetAllAccountsAsync();
            CmbBankAccount.ItemsSource = bankAccounts;
            if (bankAccounts.Count > 0)
            {
                CmbBankAccount.SelectedItem = bankAccounts.FirstOrDefault(b => b.IsDefault) ?? bankAccounts[0];
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Veriler yüklenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CmbPaymentChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PnlBankAccount == null) return;
        var tag = (CmbPaymentChannel.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Nakit";
        PnlBankAccount.Visibility = tag == "Banka Hesabı" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CmbAccounts.SelectedItem is not CariAccount account)
        {
            MessageBox.Show("Lütfen bir cari hesabı seçiniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtAmount.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Lütfen geçerli pozitif bir tutar giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAmount.Focus();
            return;
        }

        var selectedTypeItem = CmbTxType.SelectedItem as ComboBoxItem;
        var tag = selectedTypeItem?.Tag?.ToString() ?? "BORC";

        var paymentChannelTag = (CmbPaymentChannel.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Nakit";
        int? bankAccountId = null;

        if (paymentChannelTag == "Banka Hesabı")
        {
            if (CmbBankAccount.SelectedItem is BankAccount selectedBank)
            {
                bankAccountId = selectedBank.Id;
            }
            else
            {
                MessageBox.Show("Lütfen bir banka hesabı seçiniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        decimal debit = 0;
        decimal credit = 0;
        string txType;

        if (tag == "BORC")
        {
            debit = amount;
            txType = account.Type == "Tedarikçi" ? "Ödeme" : "Borç Girişi";
        }
        else
        {
            credit = amount;
            txType = "Tahsilat";
        }

        var docNo = TxtDocumentNo.Text.Trim();
        var desc = TxtDescription.Text.Trim();
        var date = DpDate.SelectedDate ?? DateTime.Today;

        try
        {
            await _cariService.AddTransactionAsync(
                cariAccountId: account.Id,
                transactionType: txType,
                debit: debit,
                credit: credit,
                documentNo: docNo,
                description: desc,
                customDate: date,
                paymentMethod: paymentChannelTag,
                bankAccountId: bankAccountId
            );

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Hareket kaydedilirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
