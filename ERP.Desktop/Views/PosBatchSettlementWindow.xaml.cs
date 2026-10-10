using System.Globalization;
using System.Windows;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class PosBatchSettlementWindow : Window
{
    private readonly List<PosTransaction> _pendingList;
    private readonly BankService _bankService;
    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    public PosBatchSettlementWindow(List<PosTransaction> pendingList, BankService bankService)
    {
        InitializeComponent();
        _pendingList = pendingList;
        _bankService = bankService;

        var totalGross = _pendingList.Sum(p => p.GrossAmount);
        var totalCommission = _pendingList.Sum(p => p.CommissionAmount);
        var totalNet = _pendingList.Sum(p => p.NetAmount);

        TxtBatchCount.Text = $"{_pendingList.Count} Fiş";
        TxtBatchGross.Text = $"₺{totalGross:N2}";
        TxtBatchCommission.Text = $"-₺{totalCommission:N2}";
        TxtBatchNet.Text = $"₺{totalNet:N2}";

        TxtTotalPaid.Text = NumberParser.FormatForEditing(totalNet);
        DpPaymentDate.SelectedDate = DateTime.Today;
        BtnConfirmBatch.Content = $"✅ Toplu Tahsilatı Onayla (₺{totalNet:N2})";

        BatchPosGrid.ItemsSource = _pendingList;

        Loaded += async (_, _) =>
        {
            var accounts = await _bankService.GetAllAccountsAsync();
            CmbAccounts.ItemsSource = accounts;
            if (accounts.Count > 0)
            {
                var sel = accounts.FirstOrDefault(a => a.BankName.Contains("Ziraat", StringComparison.OrdinalIgnoreCase))
                          ?? accounts.FirstOrDefault(a => a.IsDefault)
                          ?? accounts.FirstOrDefault(a => a.AccountName.Contains("POS", StringComparison.OrdinalIgnoreCase))
                          ?? accounts[0];
                CmbAccounts.SelectedItem = sel;
            }
        };
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingList.Count == 0)
        {
            MessageBox.Show("Tahsil edilecek bekleyen POS fişi bulunamadı.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtTotalPaid.Text, out var totalPaid) || totalPaid <= 0)
        {
            MessageBox.Show("Lütfen geçerli bir toplu tutar giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtTotalPaid.Focus();
            return;
        }

        if (CmbAccounts.SelectedItem is not BankAccount selectedAccount)
        {
            MessageBox.Show("Lütfen tahsilatın yattığı banka hesabını seçiniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var paymentDate = DpPaymentDate.SelectedDate ?? DateTime.Today;
        var posIds = _pendingList.Select(p => p.Id).ToList();

        try
        {
            await _bankService.SettleBatchPosTransactionsAsync(
                posIds,
                selectedAccount.Id,
                totalPaid,
                paymentDate.Add(DateTime.Now.TimeOfDay));

            MessageBox.Show(
                $"Toplu POS tahsilatı başarıyla gerçekleştirildi!\n\n" +
                $"Tahsil Edilen Fiş Adedi: {_pendingList.Count}\n" +
                $"Yatan Banka: {selectedAccount.BankName} - {selectedAccount.AccountName}\n" +
                $"Toplam Yatan Tutar: ₺{totalPaid:N2}",
                "Başarılı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Toplu tahsilat işlenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
