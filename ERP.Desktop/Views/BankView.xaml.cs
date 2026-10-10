using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class BankView : Page
{
    private readonly BankService _bankService = new();
    private List<BankAccount> _accounts = new();
    private List<BankTransaction> _transactions = new();
    private List<PosTransaction> _posTransactions = new();
    private static readonly CultureInfo TurkishCulture = new("tr-TR");
    private bool _isLoaded = false;

    public BankView()
    {
        InitializeComponent();

        DpTxStart.SelectedDate = DateTime.Today.AddDays(-30);
        DpTxEnd.SelectedDate = DateTime.Today;
        CmbPosStatus.SelectedIndex = 1;

        // Önbellekteki banka verilerini anında ekrana bağla
        if (AppDataCache.IsLoaded && AppDataCache.BankSummary != null)
        {
            var summary = AppDataCache.BankSummary;
            TxtTotalBalance.Text = summary.FormattedTotalBankBalance;
            TxtTodayInflow.Text = summary.FormattedTodayInflow;
            TxtTodayOutflow.Text = summary.FormattedTodayOutflow;
            TxtPendingGrossPos.Text = summary.FormattedPendingGrossPos;
            TxtPendingNetPos.Text = summary.FormattedPendingNetPos;

            if (AppDataCache.BankAccounts.Count > 0)
            {
                _accounts = new List<BankAccount>(AppDataCache.BankAccounts);
                AccountsGrid.ItemsSource = _accounts;
            }
        }

        Loaded += BankView_Loaded;
    }

    private async void BankView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAllDataAsync();
        _isLoaded = true;
    }

    private async Task LoadAllDataAsync()
    {
        try
        {
            await LoadAccountsAsync();
            await LoadTransactionsAsync();
            await LoadPosTransactionsAsync();
            LoadSummaryCards();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Banka verileri yüklenirken hata oluştu: {ex.Message}", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadSummaryCards()
    {
        var summary = _bankService.CalculateSummary(_accounts, _transactions, _posTransactions);

        TxtTotalBalance.Text = summary.FormattedTotalBankBalance;
        TxtTodayInflow.Text = summary.FormattedTodayInflow;
        TxtTodayOutflow.Text = summary.FormattedTodayOutflow;
        TxtPendingGrossPos.Text = summary.FormattedPendingGrossPos;
        TxtPendingNetPos.Text = summary.FormattedPendingNetPos;

        // Sekme 4: Bugünün Özeti
        TxtSummaryOpening.Text = summary.FormattedOpeningTotalBalance;
        decimal netChange = summary.TodayInflow - summary.TodayOutflow;
        TxtSummaryNetChange.Text = (netChange >= 0 ? "+" : "") + $"₺{netChange:N2}";
        TxtSummaryNetChange.Foreground = netChange >= 0 
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105)) 
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
        TxtSummaryClosing.Text = summary.FormattedTotalBankBalance;

        TxtSummaryPosGross.Text = summary.FormattedPendingGrossPos;
        TxtSummaryPosCommission.Text = $"-{summary.FormattedPendingPosCommission}";
        TxtSummaryPosNet.Text = summary.FormattedPendingNetPos;

        TxtSummaryPosInflow.Text = summary.FormattedTodayPosInflow;
        TxtSummaryExtInflow.Text = summary.FormattedTodayExternalInflow;
        TxtSummaryTotalInflow.Text = summary.FormattedTodayInflow;

        TxtSummaryOutflow.Text = summary.FormattedTodayOutflow;
        TxtSummaryTransferOut.Text = "₺0,00";
        TxtSummaryTotalOutflow.Text = summary.FormattedTodayOutflow;
    }

    private async Task LoadSummaryCardsAsync()
    {
        LoadSummaryCards();
        await Task.CompletedTask;
    }

    private async Task LoadAccountsAsync()
    {
        _accounts = await _bankService.GetAllAccountsAsync();
        AccountsGrid.ItemsSource = null;
        AccountsGrid.ItemsSource = _accounts;

        if (TxtTotalBalanceSub != null)
        {
            TxtTotalBalanceSub.Text = $"{_accounts.Count} aktif hesap";
        }

        // Filtre kutusuna hesapları doldur
        var filterAccounts = new List<BankAccount>
        {
            new BankAccount { Id = 0, AccountName = "Tüm Hesaplar" }
        };
        filterAccounts.AddRange(_accounts);

        int prevSelected = 0;
        if (CmbTxAccount.SelectedItem is BankAccount prev)
        {
            prevSelected = prev.Id;
        }

        CmbTxAccount.ItemsSource = filterAccounts;
        CmbTxAccount.SelectedItem = filterAccounts.FirstOrDefault(a => a.Id == prevSelected) ?? filterAccounts[0];
    }

    private async Task LoadTransactionsAsync()
    {
        int? accId = null;
        if (CmbTxAccount.SelectedItem is BankAccount selAcc && selAcc.Id > 0)
        {
            accId = selAcc.Id;
        }

        string? direction = (CmbTxDirection.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var search = TxtTxSearch.Text.Trim();

        _transactions = await _bankService.GetTransactionsAsync(
            accountId: accId,
            startDate: DpTxStart.SelectedDate,
            endDate: DpTxEnd.SelectedDate,
            direction: direction,
            searchTerm: search);

        TransactionsGrid.ItemsSource = null;
        TransactionsGrid.ItemsSource = _transactions;
    }

    private async Task LoadPosTransactionsAsync()
    {
        string? status = (CmbPosStatus.SelectedItem as ComboBoxItem)?.Content?.ToString();
        _posTransactions = await _bankService.GetPosTransactionsAsync(status: status);

        PosGrid.ItemsSource = null;
        PosGrid.ItemsSource = _posTransactions;

        var allPending = await _bankService.GetPosTransactionsAsync(status: "Bekliyor");
        var pendingGross = allPending.Sum(p => p.GrossAmount);
        var pendingComm = allPending.Sum(p => p.CommissionAmount);
        var pendingNet = allPending.Sum(p => p.NetAmount);

        TxtPendingGrossSub.Text = $"{allPending.Count} fiş bekliyor";
        TxtPendingNetSub.Text = $"Net: ₺{pendingNet:N2}";

        // Tab 3: Toplu Tutar Bilgi Göstergeleri
        TxtBatchCountLabel.Text = $"{allPending.Count} Fiş";
        TxtBatchGrossLabel.Text = $"₺{pendingGross:N2}";
        TxtBatchCommissionLabel.Text = $"-₺{pendingComm:N2}";
        TxtBatchNetLabel.Text = $"₺{pendingNet:N2}";
        BtnBatchSettle.IsEnabled = allPending.Count > 0;
    }

    #region Action Buttons

    private async void AddInflow_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanDepositToBank && !AuthService.IsAdmin)
        {
            MessageBox.Show("Bankaya para yatırma yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var win = new BankInflowWindow(_bankService) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await LoadAllDataAsync();
        }
    }

    private async void AddOutflow_Click(object sender, RoutedEventArgs e)
    {
        if (AuthService.CurrentUser?.CanAccessBank != true && !AuthService.IsAdmin)
        {
            MessageBox.Show("Bankadan para çıkışı yapma yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var win = new BankOutflowWindow(_bankService) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await LoadAllDataAsync();
        }
    }

    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        var win = new CashBankTransferWindow(_bankService) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await LoadAllDataAsync();
        }
    }

    private async void Reconciliation_Click(object sender, RoutedEventArgs e)
    {
        var win = new BankReconciliationWindow(_bankService) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await LoadAllDataAsync();
        }
    }

    private async void CreateAccount_Click(object sender, RoutedEventArgs e)
    {
        var win = new BankAccountEditWindow(null, _bankService) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await LoadAllDataAsync();
        }
    }

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is BankAccount acc)
        {
            var win = new BankAccountEditWindow(acc, _bankService) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                await LoadAllDataAsync();
            }
        }
    }

    private async void AccountReconciliation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is BankAccount acc)
        {
            var win = new BankReconciliationWindow(_bankService, acc.Id) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                await LoadAllDataAsync();
            }
        }
    }

    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanDeleteRecords && !AuthService.IsAdmin)
        {
            MessageBox.Show("Banka hesabı silme yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is Button btn && btn.DataContext is BankAccount acc)
        {
            if (acc.IsDefault)
            {
                MessageBox.Show("Varsayılan ana banka hesabı silinemez!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"'{acc.BankName} - {acc.AccountName}' hesabını silmek (arşivlemek) istediğinize emin misiniz?",
                "Hesap Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                await _bankService.DeleteAccountAsync(acc.Id);
                await LoadAllDataAsync();
            }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAllDataAsync();
    }

    private async void FilterTransactions_Click(object sender, RoutedEventArgs e)
    {
        await LoadTransactionsAsync();
    }

    private async void DeleteTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanDeleteRecords && !AuthService.IsAdmin)
        {
            MessageBox.Show("Banka hareketi silme yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is Button btn && btn.DataContext is BankTransaction tx)
        {
            var amount = tx.Direction == "Giriş" ? tx.Inflow : tx.Outflow;
            var confirm = MessageBox.Show(
                $"Bu banka hareketini silmek istediğinize emin misiniz?\n\n" +
                $"• Tarih: {tx.FormattedDate}\n" +
                $"• Hesap: {tx.BankAccountName}\n" +
                $"• İşlem Türü: {tx.TransactionType} ({tx.Direction})\n" +
                $"• Tutar: ₺{amount:N2}\n" +
                $"• Açıklama: {tx.Description}\n\n" +
                $"Not: Bu işlem silindiğinde ilgili banka hesabının sistem bakiyesi otomatik olarak düzeltilecektir.",
                "Banka Hareketini Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                await _bankService.DeleteTransactionAsync(tx.Id);
                await LoadAllDataAsync();
            }
        }
    }

    private async void TxSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded) return;
        await LoadTransactionsAsync();
    }

    private async void PosStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        await LoadPosTransactionsAsync();
    }

    private async void RefreshPos_Click(object sender, RoutedEventArgs e)
    {
        await LoadPosTransactionsAsync();
    }

    private async void SettlePos_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PosTransaction pos)
        {
            var win = new PosSettlementWindow(pos, _bankService) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                await LoadAllDataAsync();
            }
        }
    }

    private async void BatchSettlePos_Click(object sender, RoutedEventArgs e)
    {
        var pendingList = await _bankService.GetPosTransactionsAsync(status: "Bekliyor");
        if (pendingList.Count == 0)
        {
            MessageBox.Show("Tahsil edilecek bekleyen POS fişi bulunmuyor.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new PosBatchSettlementWindow(pendingList, _bankService) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await LoadAllDataAsync();
        }
    }

    #endregion
}
