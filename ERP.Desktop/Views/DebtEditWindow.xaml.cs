using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class DebtEditWindow : Window
{
    private readonly FinancialDebt? _existing;
    private readonly DebtService _debtService;
    private readonly BankService _bankService;
    private bool _isManualRemaining = false;
    private List<BankAccount> _bankAccounts = new();

    public DebtEditWindow() : this(null, new DebtService()) { }
    public DebtEditWindow(FinancialDebt? existing) : this(existing, new DebtService()) { }
    public DebtEditWindow(FinancialDebt? existing, ReminderService reminderService) : this(existing, new DebtService()) { }

    public DebtEditWindow(FinancialDebt? existing, DebtService debtService)
    {
        InitializeComponent();
        _existing = existing;
        _debtService = debtService;
        _bankService = new BankService();

        Loaded += DebtEditWindow_Loaded;
    }

    private async void DebtEditWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DpStartDate.SelectedDate = DateTime.Today;

        // Banka hesaplarını doldur
        _bankAccounts = await _bankService.GetAllAccountsAsync(false);
        CmbBankAccount.Items.Clear();
        CmbBankAccount.Items.Add(new ComboBoxItem { Content = "Yok (Bağımsız Borç)", Tag = null });
        foreach (var b in _bankAccounts)
        {
            var item = new ComboBoxItem { Content = b.DisplayText, Tag = b.Id };
            CmbBankAccount.Items.Add(item);
            if (_existing != null && _existing.BankAccountId == b.Id)
            {
                CmbBankAccount.SelectedItem = item;
            }
        }
        if (CmbBankAccount.SelectedItem == null) CmbBankAccount.SelectedIndex = 0;

        if (_existing != null)
        {
            _isManualRemaining = true;
            TxtHeading.Text = "✏️ Borç / Krediyi Düzenle";
            TxtTitle.Text = _existing.Title;
            TxtCreditor.Text = _existing.Creditor;
            TxtTotalAmount.Text = NumberParser.FormatForEditing(_existing.TotalAmount);
            TxtInstallmentAmount.Text = NumberParser.FormatForEditing(_existing.InstallmentAmount);
            TxtRemainingAmount.Text = NumberParser.FormatForEditing(_existing.RemainingAmount);
            TxtRemainingInstallments.Text = _existing.RemainingInstallments.ToString();
            TxtDayOfMonth.Text = _existing.DayOfMonth.ToString();
            DpStartDate.SelectedDate = _existing.StartDate;
            TxtNotes.Text = _existing.Notes ?? "";
            TxtInterestRate.Text = _existing.InterestRate.ToString("N2");
            TxtMinPayment.Text = NumberParser.FormatForEditing(_existing.MinPaymentAmount);

            // Borç türü seç
            foreach (ComboBoxItem item in CmbDebtType.Items)
            {
                if (item.Content.ToString() == _existing.DebtType)
                {
                    CmbDebtType.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private void TotalOrInstallment_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isManualRemaining) return;

        if (NumberParser.TryParseDecimal(TxtTotalAmount.Text, out var total) &&
            NumberParser.TryParseDecimal(TxtInstallmentAmount.Text, out var inst) && inst > 0)
        {
            TxtRemainingAmount.Text = NumberParser.FormatForEditing(total);
            int installments = (int)Math.Ceiling(total / inst);
            TxtRemainingInstallments.Text = installments.ToString();
        }
    }

    private bool _isSaving = false;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving) return;
        var btn = sender as Button;

        var title = TxtTitle.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show("Lütfen borç / kredi adını giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtTitle.Focus();
            return;
        }

        var creditor = TxtCreditor.Text.Trim();
        if (string.IsNullOrWhiteSpace(creditor))
        {
            MessageBox.Show("Lütfen alacaklı / kurum bilgisini giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtCreditor.Focus();
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtTotalAmount.Text, out var total) || total <= 0)
        {
            MessageBox.Show("Lütfen geçerli bir toplam borç tutarı giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtTotalAmount.Focus();
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtInstallmentAmount.Text, out var inst) || inst <= 0)
        {
            inst = total;
        }

        if (!NumberParser.TryParseDecimal(TxtRemainingAmount.Text, out var remaining) || remaining < 0)
        {
            remaining = total;
        }

        if (!int.TryParse(TxtRemainingInstallments.Text.Trim(), out int remInst) || remInst < 0)
        {
            remInst = (int)Math.Ceiling(remaining / (inst > 0 ? inst : 1));
        }

        if (!int.TryParse(TxtDayOfMonth.Text.Trim(), out int day) || day < 1 || day > 31)
        {
            MessageBox.Show("Lütfen 1 ile 31 arasında geçerli bir gün giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtDayOfMonth.Focus();
            return;
        }

        NumberParser.TryParseDecimal(TxtInterestRate.Text, out var intRate);
        NumberParser.TryParseDecimal(TxtMinPayment.Text, out var minPay);

        string debtType = (CmbDebtType.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Taksitli Kredi";
        int? bankAccountId = null;
        if (CmbBankAccount.SelectedItem is ComboBoxItem bi && bi.Tag is int bId)
        {
            bankAccountId = bId;
        }

        var startDate = DpStartDate.SelectedDate ?? DateTime.Today;
        var notes = TxtNotes.Text.Trim();

        var debt = _existing ?? new FinancialDebt();
        debt.Title = title;
        debt.Creditor = creditor;
        debt.DebtType = debtType;
        debt.BankAccountId = bankAccountId;
        debt.TotalAmount = total;
        debt.InstallmentAmount = inst;
        debt.RemainingAmount = remaining;
        debt.PrincipalAmount = remaining;
        debt.TotalInstallments = (int)Math.Ceiling(total / (inst > 0 ? inst : 1));
        debt.RemainingInstallments = remInst;
        debt.DayOfMonth = day;
        debt.StartDate = startDate;
        debt.Notes = notes;
        debt.InterestRate = intRate;
        debt.MinPaymentAmount = minPay;

        _isSaving = true;
        if (btn != null)
        {
            btn.IsEnabled = false;
            btn.Content = "⏳ Kaydediliyor...";
        }

        try
        {
            await _debtService.SaveOrUpdateDebtAsync(debt, AuthService.CurrentUser?.FullName ?? "Yönetici");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isSaving = false;
            if (btn != null)
            {
                btn.IsEnabled = true;
                btn.Content = "💾 Kaydet";
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
