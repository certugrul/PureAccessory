using System.Windows;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class ReminderPaymentWindow : Window
{
    private readonly FinancialReminder _reminder;
    private readonly ReminderService _reminderService;
    private readonly BankService _bankService;
    private readonly CashService _cashService;
    private List<BankAccount> _bankAccounts = new();

    public ReminderPaymentWindow(FinancialReminder reminder, ReminderService reminderService)
    {
        InitializeComponent();
        _reminder = reminder;
        _reminderService = reminderService;
        _bankService = new BankService();
        _cashService = new CashService();

        TxtReminderTitle.Text = reminder.Title;
        TxtReminderCategory.Text = $"{reminder.CategoryIcon} Kategori: {reminder.Category}";
        TxtReminderDueDate.Text = $"Vade Tarihi: {reminder.DueDate:dd.MM.yyyy}";
        TxtReminderAmount.Text = reminder.FormattedAmount;
        DpPaymentDate.SelectedDate = DateTime.Today;
        TxtNotes.Text = $"{reminder.Title} Masraf Ödemesi ({DateTime.Today:MMMM yyyy})";

        Loaded += ReminderPaymentWindow_Loaded;
    }

    private async void ReminderPaymentWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _bankAccounts = await _bankService.GetAllAccountsAsync();
            CmbBankAccounts.ItemsSource = _bankAccounts;
            CmbBankAccounts.DisplayMemberPath = "DisplayText";

            if (_bankAccounts.Count > 0)
            {
                var def = _bankAccounts.FirstOrDefault(b => b.IsDefault) ?? _bankAccounts[0];
                CmbBankAccounts.SelectedItem = def;
            }

            var cash = await _cashService.GetCashSummaryAsync(DateTime.Today);
            TxtCashStatus.Text = cash.IsOpen 
                ? $"🟢 Kasa Açık — Mevcut Nakit: {cash.FormattedCurrentBalance}" 
                : "🔴 Kasa Kapalı (Gün Açılışı Yapılmadı)";
        }
        catch { }
    }

    private void PaymentSource_Changed(object sender, RoutedEventArgs e)
    {
        if (PnlBankSelect == null) return;
        PnlBankSelect.Visibility = RbBank.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        string paymentSource = RbBank.IsChecked == true ? "Banka Hesabı" : "Nakit";
        int? bankAccountId = null;
        string? bankAccountName = null;

        if (paymentSource == "Banka Hesabı")
        {
            if (CmbBankAccounts.SelectedItem is not BankAccount selectedBank)
            {
                MessageBox.Show("Lütfen ödemenin yapılacağı banka hesabını seçiniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bankAccountId = selectedBank.Id;
            bankAccountName = $"{selectedBank.BankName} - {selectedBank.AccountName}";
        }

        var payDate = DpPaymentDate.SelectedDate ?? DateTime.Today;
        var notes = TxtNotes.Text.Trim();

        try
        {
            var updated = await _reminderService.MarkAsPaidAsync(
                reminderId: _reminder.Id,
                paymentSource: paymentSource,
                bankAccountId: bankAccountId,
                bankAccountName: bankAccountName,
                paymentDate: payDate,
                notes: notes);

            if (updated != null)
            {
                string targetSource = paymentSource == "Banka Hesabı" ? bankAccountName! : "Merkez Nakit Kasa";
                string debtInfo = "";
                if (updated.DebtId.HasValue && updated.DebtId.Value > 0)
                {
                    if (updated.RemainingDebtAfter.HasValue)
                    {
                        if (updated.RemainingDebtAfter.Value <= 0)
                        {
                            debtInfo = "\n\n🎉 TEBRİKLER! Bu borç tamamen ödendi ve kapandı.\nArtık gelecek ayların takviminde çıkmayacaktır.";
                        }
                        else
                        {
                            debtInfo = $"\n\n📉 Borçtan düşüldü! Kalan borç bakiyesi: ₺{updated.RemainingDebtAfter.Value:N2}";
                        }
                    }
                }

                MessageBox.Show(
                    $"Ödeme başarıyla işlendi ve kaydedildi!\n\n" +
                    $"• Fatura: {updated.InvoiceNo} (Masraf Faturası)\n" +
                    $"• Ödenen Tutar: {updated.FormattedAmount}\n" +
                    $"• Ödeme Kaynağı: {targetSource}\n" +
                    $"• Bakiye otomatik olarak düşüldü.{debtInfo}",
                    "Ödeme Başarılı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Hatırlatma kaydı bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ödeme kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
