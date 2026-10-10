using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class BankAccountEditWindow : Window
{
    private readonly BankAccount _account;
    private readonly BankService _bankService;
    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    public BankAccountEditWindow(BankAccount? account, BankService bankService)
    {
        InitializeComponent();
        _bankService = bankService;
        _account = account ?? new BankAccount();

        if (account != null && account.Id > 0)
        {
            TxtTitle.Text = "✏️ Banka Hesabını Düzenle";
            CmbBankName.Text = account.BankName;
            TxtAccountName.Text = account.AccountName;
            TxtIban.Text = account.Iban;
            TxtAccountNumber.Text = account.AccountNumber;
            TxtBranch.Text = account.Branch;
            TxtOpeningBalance.Text = NumberParser.FormatForEditing(account.OpeningBalance);
            TxtOpeningBalance.IsEnabled = false; // Açılış bakiyesi sonradan değiştirilemez, hareket ile güncellenir
            TxtCommissionRate.Text = NumberParser.FormatForEditing(account.PosCommissionRate);
            TxtValourDays.Text = account.PosValourDays.ToString();
            ChkIsDefault.IsChecked = account.IsDefault;

            foreach (ComboBoxItem item in CmbCurrency.Items)
            {
                if (item.Tag?.ToString() == account.Currency)
                {
                    CmbCurrency.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var bankName = CmbBankName.Text.Trim();
        var accountName = TxtAccountName.Text.Trim();

        if (string.IsNullOrWhiteSpace(bankName))
        {
            MessageBox.Show("Lütfen banka adını giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            CmbBankName.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(accountName))
        {
            MessageBox.Show("Lütfen hesap adını giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAccountName.Focus();
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtCommissionRate.Text, out var commRate))
        {
            commRate = 3.99m;
        }

        if (!int.TryParse(TxtValourDays.Text, out var valourDays))
        {
            valourDays = 2;
        }

        decimal openingBal = 0;
        if (_account.Id == 0)
        {
            NumberParser.TryParseDecimal(TxtOpeningBalance.Text, out openingBal);
        }

        string currency = "TRY";
        if (CmbCurrency.SelectedItem is ComboBoxItem curItem && curItem.Tag != null)
        {
            currency = curItem.Tag.ToString() ?? "TRY";
        }

        _account.BankName = bankName;
        _account.AccountName = accountName;
        _account.Iban = TxtIban.Text.Trim();
        _account.AccountNumber = TxtAccountNumber.Text.Trim();
        _account.Branch = TxtBranch.Text.Trim();
        _account.Currency = currency;
        _account.PosCommissionRate = commRate;
        _account.PosValourDays = valourDays;
        _account.IsDefault = ChkIsDefault.IsChecked == true;

        if (_account.Id == 0)
        {
            _account.OpeningBalance = openingBal;
            _account.CurrentBalance = openingBal;
            _account.ActualBalance = openingBal;
        }

        try
        {
            await _bankService.SaveOrUpdateAccountAsync(_account);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Banka hesabı kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
