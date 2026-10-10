using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class DebtPaymentWindow : Window
{
    private readonly FinancialDebt _debt;
    private readonly DebtService _debtService;
    private readonly BankService _bankService;
    private List<BankAccount> _bankAccounts = new();

    public DebtPaymentWindow(FinancialDebt debt)
    {
        InitializeComponent();
        _debt = debt;
        _debtService = new DebtService();
        _bankService = new BankService();

        Loaded += DebtPaymentWindow_Loaded;
    }

    private async void DebtPaymentWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Bilgileri Doldur
        TxtDebtTitle.Text = $"{_debt.Title} ({_debt.Creditor})";
        TxtPrincipal.Text = _debt.FormattedPrincipal;
        TxtAccruedInterestTax.Text = $"₺{_debt.AccruedInterest + _debt.AccruedTax:N2}";
        TxtTotalDebt.Text = _debt.FormattedTotalPayable;

        DpPaymentDate.SelectedDate = DateTime.Today;

        // 2. Banka Hesaplarını Yükle
        _bankAccounts = await _bankService.GetAllAccountsAsync(false);
        CmbPaymentSource.Items.Clear();
        CmbPaymentSource.Items.Add(new ComboBoxItem { Content = "💵 Nakit Kasa (Merkez Kasa)", Tag = null });

        foreach (var ba in _bankAccounts.Where(b => b.IsDeposit))
        {
            CmbPaymentSource.Items.Add(new ComboBoxItem
            {
                Content = $"🏦 {ba.BankName} - {ba.AccountName} (Bakiye: {ba.FormattedCurrentBalance})",
                Tag = ba.Id
            });
        }
        CmbPaymentSource.SelectedIndex = 0;

        // 3. Varsayılan Tutar Olarak Taksit veya Toplam Tutarı Koy
        decimal defAmount = _debt.InstallmentAmount > 0 ? Math.Min(_debt.InstallmentAmount, _debt.TotalPayableDebt) : _debt.TotalPayableDebt;
        TxtPaymentAmount.Text = defAmount.ToString("N2");
        TxtNotes.Text = $"{_debt.Title} Ödemesi";
    }

    private void BtnFullPay_Click(object sender, RoutedEventArgs e)
    {
        TxtPaymentAmount.Text = _debt.TotalPayableDebt.ToString("N2");
    }

    private void BtnMinPay_Click(object sender, RoutedEventArgs e)
    {
        decimal minPay = _debt.MinPaymentAmount > 0 
            ? _debt.MinPaymentAmount 
            : (_debt.InstallmentAmount > 0 ? _debt.InstallmentAmount : Math.Round(_debt.TotalPayableDebt * 0.20m, 2));
        TxtPaymentAmount.Text = minPay.ToString("N2");
    }

    private void BtnInstallmentPay_Click(object sender, RoutedEventArgs e)
    {
        decimal inst = _debt.InstallmentAmount > 0 ? _debt.InstallmentAmount : _debt.TotalPayableDebt;
        TxtPaymentAmount.Text = inst.ToString("N2");
    }

    private void TxtPaymentAmount_TextChanged(object sender, TextChangedEventArgs e)
    {
        RecalculateSimulation();
    }

    private void DpPaymentDate_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        RecalculateSimulation();
    }

    private void CmbPaymentSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Kaynak değiştiğinde
    }

    private void RecalculateSimulation()
    {
        if (TxtSimInterest == null) return;

        if (decimal.TryParse(TxtPaymentAmount.Text?.Replace("₺", "")?.Trim(), out decimal paid) && paid > 0)
        {
            var breakdown = PaymentAllocationService.Allocate(
                totalPaid: paid,
                accruedInterest: _debt.AccruedInterest,
                accruedTax: _debt.AccruedTax,
                currentPrincipal: _debt.PrincipalAmount);

            TxtSimInterest.Text = $"₺{breakdown.ToInterest:N2}";
            TxtSimTax.Text = $"₺{breakdown.ToTax:N2}";
            TxtSimPrincipal.Text = $"₺{breakdown.ToPrincipal:N2}";
            TxtSimRemainingPrincipal.Text = $"₺{breakdown.RemainingPrincipal:N2}";
            TxtSimRemainingTotal.Text = $"₺{breakdown.RemainingTotalDebt:N2}";
        }
        else
        {
            TxtSimInterest.Text = "₺0,00";
            TxtSimTax.Text = "₺0,00";
            TxtSimPrincipal.Text = "₺0,00";
            TxtSimRemainingPrincipal.Text = _debt.FormattedPrincipal;
            TxtSimRemainingTotal.Text = _debt.FormattedTotalPayable;
        }
    }

    private async void ConfirmPayment_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(TxtPaymentAmount.Text?.Replace("₺", "")?.Trim(), out decimal amount) || amount <= 0)
        {
            MessageBox.Show("Lütfen geçerli bir ödeme tutarı girin.", "Geçersiz Tutar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DateTime paymentDate = DpPaymentDate.SelectedDate ?? DateTime.Today;

        int? bankAccountId = null;
        if (CmbPaymentSource.SelectedItem is ComboBoxItem item && item.Tag is int bId)
        {
            bankAccountId = bId;

            // Bakiye kontrolü
            var selectedBank = _bankAccounts.FirstOrDefault(b => b.Id == bId);
            if (selectedBank != null && selectedBank.CurrentBalance < amount)
            {
                var ask = MessageBox.Show(
                    $"Seçilen banka hesabının güncel bakiyesi (₺{selectedBank.CurrentBalance:N2}), ödeme tutarından (₺{amount:N2}) düşüktür.\n\n" +
                    "Yine de işleme devam etmek ve hesabı eksiye / KMH'ye geçirmek istiyor musunuz?",
                    "Yetersiz Banka Bakiyesi Uyarısı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (ask != MessageBoxResult.Yes) return;
            }
        }

        string paymentType = amount >= _debt.TotalPayableDebt ? "Tam" : (amount == _debt.MinPaymentAmount ? "Asgari" : "Kısmi");
        string? notes = TxtNotes.Text?.Trim();

        try
        {
            BtnConfirmPayment.IsEnabled = false;

            var breakdown = await _debtService.ProcessPaymentAsync(
                debtId: _debt.Id,
                paymentAmount: amount,
                paymentType: paymentType,
                fromBankAccountId: bankAccountId,
                paymentDate: paymentDate,
                notes: notes,
                userName: AuthService.CurrentUser?.FullName ?? "Yönetici");

            MessageBox.Show(
                $"Ödeme başarıyla işlendi ve veritabanına kaydedildi!\n\n" +
                $"Ödenen Tutar: ₺{amount:N2}\n" +
                $"• Kapatılan Faiz: ₺{breakdown.ToInterest:N2}\n" +
                $"• Kapatılan Vergi: ₺{breakdown.ToTax:N2}\n" +
                $"• Anaparadan Düşen: ₺{breakdown.ToPrincipal:N2}\n\n" +
                $"Kalan Saf Anapara: ₺{breakdown.RemainingPrincipal:N2}\n" +
                $"Kalan Toplam Borç: ₺{breakdown.RemainingTotalDebt:N2}",
                "Ödeme Başarılı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ödeme işlemi sırasında bir hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnConfirmPayment.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
