using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class CariView : Page
{
    private readonly CariService _cariService;
    private List<CariAccount> _allAccounts = new();

    public CariView()
    {
        InitializeComponent();
        _cariService = new CariService();

        DpStart.SelectedDate = DateTime.Today.AddDays(-30);
        DpEnd.SelectedDate = DateTime.Today;

        // Önbellekteki carileri ve bakiye özetini anında ekrana yansıt
        if (AppDataCache.IsLoaded && AppDataCache.CariAccounts.Count > 0)
        {
            _allAccounts = new List<CariAccount>(AppDataCache.CariAccounts);
            AccountsGrid.ItemsSource = _allAccounts;

            var cariFilterList = new List<CariAccount> { new CariAccount { Id = 0, Name = "-- Tüm Cariler --" } };
            cariFilterList.AddRange(_allAccounts);
            CmbCariFilter.ItemsSource = cariFilterList;
            CmbCariFilter.SelectedIndex = 0;

            var totalReceivables = _allAccounts.Where(a => a.Balance > 0).Sum(a => a.Balance);
            var totalPayables = _allAccounts.Where(a => a.Balance < 0).Sum(a => Math.Abs(a.Balance));
            var netBalance = totalReceivables - totalPayables;

            TxtTotalReceivables.Text = $"₺{totalReceivables:N2}";
            TxtTotalPayables.Text = $"₺{totalPayables:N2}";
            TxtNetBalance.Text = $"₺{netBalance:N2}";
        }

        Loaded += CariView_Loaded;
    }

    private async void CariView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAllDataAsync();
    }

    private async Task LoadAllDataAsync()
    {
        try
        {
            await LoadAccountsAsync();
            await LoadTransactionsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cari verileri yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadAccountsAsync()
    {
        _allAccounts = await _cariService.GetAllAccountsAsync();

        // Cari filtre combobox doldur
        var cariFilterList = new List<CariAccount> { new CariAccount { Id = 0, Name = "-- Tüm Cariler --" } };
        cariFilterList.AddRange(_allAccounts);
        CmbCariFilter.ItemsSource = cariFilterList;
        if (CmbCariFilter.SelectedIndex < 0) CmbCariFilter.SelectedIndex = 0;

        // Özet Kartları Güncelle
        // Müşteri alacakları: Pozitif bakiye (biz alacaklıyız)
        var totalReceivables = _allAccounts.Where(a => a.Balance > 0).Sum(a => a.Balance);
        // Tedarikçi borçları: Negatif bakiye (biz borçluyuz)
        var totalPayables = _allAccounts.Where(a => a.Balance < 0).Sum(a => Math.Abs(a.Balance));
        var netBalance = totalReceivables - totalPayables;

        TxtTotalReceivables.Text = $"₺{totalReceivables:N2}";
        TxtTotalPayables.Text = $"₺{totalPayables:N2}";
        TxtNetBalance.Text = $"₺{netBalance:N2}";
        TxtNetBalance.Foreground = netBalance >= 0 
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235)) 
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
        TxtTotalAccounts.Text = _allAccounts.Count.ToString();

        ApplyAccountFilters();
    }

    private void ApplyAccountFilters()
    {
        var filtered = _allAccounts.AsEnumerable();

        var selectedType = (CmbTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(selectedType) && selectedType != "Tümü")
        {
            filtered = filtered.Where(a => a.Type == selectedType);
        }

        var search = TxtAccountSearch?.Text?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(a =>
                (a.Name?.ToLowerInvariant().Contains(search) ?? false) ||
                (a.Code?.ToLowerInvariant().Contains(search) ?? false) ||
                (a.Phone?.ToLowerInvariant().Contains(search) ?? false) ||
                (a.TaxNo?.ToLowerInvariant().Contains(search) ?? false));
        }

        AccountsGrid.ItemsSource = filtered.ToList();
    }

    private async Task LoadTransactionsAsync()
    {
        int? accountId = null;
        if (CmbCariFilter.SelectedItem is CariAccount selAcc && selAcc.Id > 0)
        {
            accountId = selAcc.Id;
        }

        var startDate = DpStart.SelectedDate;
        var endDate = DpEnd.SelectedDate;
        var search = TxtTxSearch?.Text?.Trim();

        var transactions = await _cariService.GetTransactionsAsync(accountId, startDate, endDate, search);
        TransactionsGrid.ItemsSource = transactions;
    }

    private void TypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountsGrid != null) ApplyAccountFilters();
    }

    private void AccountSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (AccountsGrid != null) ApplyAccountFilters();
    }

    private async void FilterTx_Click(object sender, RoutedEventArgs e)
    {
        await LoadTransactionsAsync();
    }

    private async void TxSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        await LoadTransactionsAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAllDataAsync();
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var win = new CariAccountEditWindow(null, _cariService)
        {
            Owner = Window.GetWindow(this)
        };
        if (win.ShowDialog() == true)
        {
            await LoadAllDataAsync();
        }
    }

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CariAccount account)
        {
            var win = new CariAccountEditWindow(account, _cariService)
            {
                Owner = Window.GetWindow(this)
            };
            if (win.ShowDialog() == true)
            {
                await LoadAllDataAsync();
            }
        }
    }

    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CariAccount account)
        {
            var result = MessageBox.Show(
                $"'{account.Name}' isimli cari kartı silmek istediğinize emin misiniz?",
                "Cari Silme Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _cariService.DeleteAccountAsync(account.Id);
                await LoadAllDataAsync();
            }
        }
    }

    private async void AddTransaction_Click(object sender, RoutedEventArgs e)
    {
        var win = new CariTransactionAddWindow(_cariService)
        {
            Owner = Window.GetWindow(this)
        };
        if (win.ShowDialog() == true)
        {
            await LoadAllDataAsync();
        }
    }

    private async void DeleteTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CariTransaction tx)
        {
            var changeText = tx.Debit > 0 
                ? $"Borç: ₺{tx.Debit:N2} (Bakiye düşülecektir)" 
                : $"Alacak: ₺{tx.Credit:N2} (Bakiye artırılacaktır)";

            var result = MessageBox.Show(
                $"Bu cari hareketi silmek istediğinize emin misiniz?\n\n" +
                $"Cari: {tx.CariAccountName}\n" +
                $"Tarih: {tx.FormattedDate}\n" +
                $"Tür: {tx.TransactionType}\n" +
                $"Etki: {changeText}\n\n" +
                $"Bu işlem silindiğinde cari hesap bakiyesi otomatik olarak tersine düzeltilecektir.",
                "Cari Hareketini Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var success = await _cariService.DeleteTransactionAsync(tx.Id);
                if (success)
                {
                    MessageBox.Show("Cari hareketi başarıyla silindi ve bakiye güncellendi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadAllDataAsync();
                }
                else
                {
                    MessageBox.Show("Cari hareketi bulunamadı veya silinemedi.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
