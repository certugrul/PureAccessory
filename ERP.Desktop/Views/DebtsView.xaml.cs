using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;
using System.Data.Common;

namespace ERP.Desktop.Views;

public partial class DebtsView : Page
{
    private readonly DebtService _debtService;
    private readonly BankService _bankService;
    private readonly InterestEngine _interestEngine;
    private readonly DatabaseService _dbService;

    private List<FinancialDebt> _allDebts = new();
    private List<BankAccount> _cardAccounts = new();

    public DebtsView()
    {
        _dbService = new DatabaseService();
        _interestEngine = new InterestEngine(_dbService);
        _debtService = new DebtService(_dbService, _interestEngine);
        _bankService = new BankService(_dbService);

        InitializeComponent();

        Loaded += async (s, e) => await LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        try
        {
            if (_debtService == null || _bankService == null) return;

            // 1. Borçları Yükle
            bool showCompleted = ChkShowCompleted?.IsChecked == true;
            _allDebts = await _debtService.GetAllDebtsAsync(includeInactive: showCompleted);

            // 2. Kredi Kartı ve KMH Banka Hesaplarını Yükle
            var allAccounts = await _bankService.GetAllAccountsAsync(false);
            _cardAccounts = allAccounts.Where(a => a.IsCreditCard || a.IsKmh).ToList();
            if (DgCardsKmh != null)
            {
                DgCardsKmh.ItemsSource = _cardAccounts;
            }

            // 3. İstatistikleri Güncelle
            var summary = await _debtService.GetDebtSummaryAsync();
            if (TxtTotalDebt != null) TxtTotalDebt.Text = summary.FormattedTotalDebt;
            if (TxtTotalPrincipal != null) TxtTotalPrincipal.Text = summary.FormattedTotalPrincipal;
            if (TxtTotalInterestTax != null) TxtTotalInterestTax.Text = $"₺{summary.TotalAccruedInterest + summary.TotalAccruedTax:N2}";
            if (TxtInterestTaxSub != null) TxtInterestTaxSub.Text = $"Faiz: ₺{summary.TotalAccruedInterest:N2} | Vergi: ₺{summary.TotalAccruedTax:N2}";
            if (TxtTotalCreditCard != null) TxtTotalCreditCard.Text = summary.FormattedTotalCreditCardDebt;
            if (TxtTotalKmh != null) TxtTotalKmh.Text = summary.FormattedTotalKmhDebt;

            // 4. Filtrele ve Tabloya Bas
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Veriler yüklenirken hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (_allDebts == null || DgDebts == null || TxtSearchDebt == null || CmbTypeFilter == null || ChkShowCompleted == null || TxtDebtCount == null)
            return;

        var filtered = _allDebts.AsEnumerable();

        if (ChkShowCompleted.IsChecked != true)
        {
            filtered = filtered.Where(d => !d.IsCompleted);
        }

        // Metin Arama
        string search = TxtSearchDebt.Text?.Trim().ToLower() ?? "";
        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(d => 
                (d.Title != null && d.Title.ToLower().Contains(search)) ||
                (d.Creditor != null && d.Creditor.ToLower().Contains(search)) ||
                (d.BankAccountName != null && d.BankAccountName.ToLower().Contains(search)));
        }

        // Tür Filtresi
        if (CmbTypeFilter.SelectedIndex > 0 && CmbTypeFilter.SelectedItem is ComboBoxItem item && item.Content != null)
        {
            string sel = item.Content.ToString()!;
            filtered = filtered.Where(d => d.DebtType == sel || (sel == "Şahıs / Diğer" && d.DebtType != "Kredi Kartı" && d.DebtType != "KMH / Ek Hesap" && d.DebtType != "Taksitli Kredi"));
        }

        var list = filtered.ToList();
        DgDebts.ItemsSource = list;
        TxtDebtCount.Text = $"{list.Count} Borç Kaydı";
    }

    private void TxtSearchDebt_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void CmbTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void ChkShowCompleted_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadDataAsync();

    // --- FAİZ TAHAKKUKLARI HESAPLAMA ---
    private async void RecalculateAccruals_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Tüm aktif borçlar, kredi kartları ve KMH hesapları için bugüne kadarki faiz ve yasal vergi (KKDF %15 + BSMV %5) tahakkukları hesaplansın mı?\n\n" +
            "Not: Bu işlem banka hesabınızdan para DÜŞMEZ. Sadece güncel borç yükünüzü ve tahakkuk etmiş faizleri veritabanına yansıtır.",
            "Faiz Tahakkuk Motoru",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            int calculatedCount = 0;
            decimal totalAccruedInterest = 0;
            decimal totalAccruedTax = 0;

            await using var conn = _dbService.CreateConnection();
            await conn.OpenAsync();

            foreach (var debt in _allDebts.Where(d => d.IsActive && !d.IsCompleted && d.PrincipalAmount > 0))
            {
                await using var tran = await conn.BeginTransactionAsync();
                try
                {
                    var accrual = await _interestEngine.AccrueInterestAsync(debt.Id, DateTime.Today, conn, tran, AuthService.CurrentUser?.FullName ?? "Yönetici");
                    if (accrual != null)
                    {
                        await tran.CommitAsync();
                        calculatedCount++;
                        totalAccruedInterest += accrual.CalculatedInterest;
                        totalAccruedTax += accrual.TotalTax;
                    }
                    else
                    {
                        await tran.RollbackAsync();
                    }
                }
                catch
                {
                    await tran.RollbackAsync();
                }
            }

            await LoadDataAsync();

            MessageBox.Show(
                $"{calculatedCount} adet borç için faiz tahakkuku başarıyla uygulandı.\n\n" +
                $"Tahakkuk Eden Net Faiz: ₺{totalAccruedInterest:N2}\n" +
                $"Tahakkuk Eden Vergi: ₺{totalAccruedTax:N2}\n" +
                $"Toplam Eklenen Borç: ₺{totalAccruedInterest + totalAccruedTax:N2}",
                "Faiz Tahakkuku Tamamlandı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Faiz hesaplanırken hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- FAİZ ORANLARI PENCERESİ ---
    private void InterestRates_Click(object sender, RoutedEventArgs e)
    {
        var win = new InterestRatesWindow { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }

    // --- BORÇ EKLE ---
    private async void CreateDebt_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanManageDebts)
        {
            MessageBox.Show("Borç ve kredi kartı tanımlama yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var win = new DebtEditWindow { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await LoadDataAsync();
        }
    }

    // --- BORÇ DÜZENLE ---
    private async void DebtEditRow_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanManageDebts)
        {
            MessageBox.Show("Borç ve kredi kartı düzenleme yetkiniz bulunmamaktadır!", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is Button btn && btn.Tag is FinancialDebt debt)
        {
            var win = new DebtEditWindow(debt) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                await LoadDataAsync();
            }
        }
    }

    // --- BORÇ SİL ---
    private async void DebtDeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanDeleteRecords && !AuthService.IsAdmin)
        {
            MessageBox.Show("Borç kaydı silme yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is Button btn && btn.Tag is FinancialDebt debt)
        {
            var res = MessageBox.Show(
                $"'{debt.Title}' başlıklı borç kaydını silmek / pasife almak istediğinizden emin misiniz?",
                "Borç Silme Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                await _debtService.DeleteDebtAsync(debt.Id, AuthService.CurrentUser?.FullName ?? "Yönetici");
                await LoadDataAsync();
            }
        }
    }

    // --- HAREKET GEÇMİŞİ ---
    private void DebtHistoryRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is FinancialDebt debt)
        {
            var win = new DebtHistoryWindow(debt) { Owner = Window.GetWindow(this) };
            win.ShowDialog();
        }
    }

    // --- HIZLI ÖDEME YAP BUTONU ---
    private async void MakePayment_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanManageDebts)
        {
            MessageBox.Show("Borç ve kredi kartı ödeme işlemi yapma yetkiniz bulunmamaktadır!", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedDebt = DgDebts.SelectedItem as FinancialDebt ?? _allDebts.FirstOrDefault(d => !d.IsCompleted);
        if (selectedDebt == null)
        {
            MessageBox.Show("Lütfen ödeme yapmak istediğiniz borcu listeden seçin.", "Borç Seçimi Gerekli", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new DebtPaymentWindow(selectedDebt) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await LoadDataAsync();
        }
    }

    // --- SATIRDAN ÖDEME YAP ---
    private async void DebtPayRow_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanManageDebts)
        {
            MessageBox.Show("Borç ve kredi kartı ödeme işlemi yapma yetkiniz bulunmamaktadır!", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is Button btn && btn.Tag is FinancialDebt debt)
        {
            var win = new DebtPaymentWindow(debt) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                await LoadDataAsync();
            }
        }
    }

    // --- KART / KMH TABLOSUNDAN ÖDEME YAP ---
    private async void CardPay_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanManageDebts)
        {
            MessageBox.Show("Borç ve kredi kartı ödeme işlemi yapma yetkiniz bulunmamaktadır!", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (sender is Button btn && btn.Tag is BankAccount account)
        {
            // Bu karta/hesaba bağlı borç kaydını bul veya dinamik oluştur
            var linkedDebt = _allDebts.FirstOrDefault(d => d.BankAccountId == account.Id && !d.IsCompleted);
            if (linkedDebt == null)
            {
                linkedDebt = new FinancialDebt
                {
                    Title = account.DisplayText,
                    Creditor = account.BankName,
                    DebtType = account.AccountType,
                    BankAccountId = account.Id,
                    TotalAmount = account.CreditLimit,
                    PrincipalAmount = account.CurrentDebt,
                    RemainingAmount = account.CurrentDebt,
                    DayOfMonth = account.DueDay ?? 15,
                    Status = "Devam Ediyor"
                };
                linkedDebt = await _debtService.SaveOrUpdateDebtAsync(linkedDebt);
            }

            var win = new DebtPaymentWindow(linkedDebt) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                await LoadDataAsync();
            }
        }
    }

    // --- SEKMELER DEĞİŞTİĞİNDE YÜKLE ---
    private async void TabsMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != TabsMain || TabsMain == null || _dbService == null) return;

        if (TabsMain.SelectedIndex == 2) // Hareket Geçmişi
        {
            await LoadAllTransactionsAsync();
        }
        else if (TabsMain.SelectedIndex == 3) // Audit Logs
        {
            await LoadAuditLogsAsync();
        }
    }

    private async Task LoadAllTransactionsAsync()
    {
        try
        {
            var list = new List<DebtTransaction>();
            await using var conn = _dbService.CreateConnection();
            await conn.OpenAsync();
            var sql = @"
                SELECT TOP 100 Id, DebtId, TransactionDate, TransactionType, Amount, PrincipalChange, InterestChange, TaxChange, 
                               PreviousBalance, NewBalance, Description, ReferenceId, ReferenceType, UserId, CreatedAt
                FROM DebtTransactions
                ORDER BY TransactionDate DESC, Id DESC";

            await using var cmd = conn.CreateCommand(sql);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new DebtTransaction
                {
                    Id = reader.GetInt64(0),
                    DebtId = reader.GetInt32(1),
                    TransactionDate = reader.GetDateTime(2),
                    TransactionType = reader.GetString(3),
                    Amount = reader.GetDecimal(4),
                    PrincipalChange = reader.GetDecimal(5),
                    InterestChange = reader.GetDecimal(6),
                    TaxChange = reader.GetDecimal(7),
                    PreviousBalance = reader.GetDecimal(8),
                    NewBalance = reader.GetDecimal(9),
                    Description = reader.IsDBNull(10) ? null : reader.GetString(10),
                    ReferenceId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
                    ReferenceType = reader.IsDBNull(12) ? null : reader.GetString(12),
                    UserId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    CreatedAt = reader.GetDateTime(14)
                });
            }
            DgAllTransactions.ItemsSource = list;
        }
        catch { }
    }

    private async Task LoadAuditLogsAsync()
    {
        try
        {
            var list = new List<FinancialAuditLog>();
            await using var conn = _dbService.CreateConnection();
            await conn.OpenAsync();
            var sql = @"
                SELECT TOP 100 Id, ActionType, EntityName, EntityId, BankAccountId, DebtId, OldValue, NewValue, Description, UserId, UserName, CreatedAt
                FROM FinancialAuditLogs
                ORDER BY CreatedAt DESC, Id DESC";

            await using var cmd = conn.CreateCommand(sql);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new FinancialAuditLog
                {
                    Id = reader.GetInt64(0),
                    ActionType = reader.GetString(1),
                    EntityName = reader.GetString(2),
                    EntityId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    BankAccountId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    DebtId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    OldValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                    NewValue = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Description = reader.GetString(8),
                    UserId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    UserName = reader.GetString(10),
                    CreatedAt = reader.GetDateTime(11)
                });
            }
            DgAuditLogs.ItemsSource = list;
        }
        catch { }
    }

    private async void RefreshTransactions_Click(object sender, RoutedEventArgs e) => await LoadAllTransactionsAsync();
    private async void RefreshAuditLogs_Click(object sender, RoutedEventArgs e) => await LoadAuditLogsAsync();
}
