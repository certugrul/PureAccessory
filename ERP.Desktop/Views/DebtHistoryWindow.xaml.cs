using System.Windows;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class DebtHistoryWindow : Window
{
    private readonly FinancialDebt _debt;
    private readonly DebtService _debtService;

    public DebtHistoryWindow(FinancialDebt debt)
    {
        InitializeComponent();
        _debt = debt;
        _debtService = new DebtService();

        Loaded += DebtHistoryWindow_Loaded;
    }

    private async void DebtHistoryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        TxtTitle.Text = _debt.Title;
        TxtSubtitle.Text = $"{_debt.Creditor} | Tür: {_debt.DebtType} | Anapara: {_debt.FormattedPrincipal}";
        TxtTotalDebt.Text = _debt.FormattedTotalPayable;

        try
        {
            var transactions = await _debtService.GetDebtTransactionsAsync(_debt.Id);
            DgTransactions.ItemsSource = transactions;

            var accruals = await _debtService.GetInterestAccrualsAsync(_debt.Id);
            DgAccruals.ItemsSource = accruals;

            var payments = await _debtService.GetPaymentsAsync(_debt.Id);
            DgPayments.ItemsSource = payments;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Geçmiş yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
