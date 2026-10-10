using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class FinancialTemplatesWindow : Window
{
    private readonly ReminderService _reminderService;

    public FinancialTemplatesWindow(ReminderService reminderService)
    {
        InitializeComponent();
        _reminderService = reminderService;

        Loaded += FinancialTemplatesWindow_Loaded;
    }

    private async void FinancialTemplatesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAllDataAsync();
    }

    private async Task LoadAllDataAsync()
    {
        try
        {
            await LoadExpensesAsync();
            await LoadDebtsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Veriler yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadExpensesAsync()
    {
        var list = await _reminderService.GetAllGeneralExpensesAsync();
        ExpensesGrid.ItemsSource = list;
    }

    private async Task LoadDebtsAsync()
    {
        var list = await _reminderService.GetAllDebtsAsync();
        DebtsGrid.ItemsSource = list;

        var totalDebt = list.Sum(d => d.TotalAmount);
        var remainingDebt = list.Sum(d => d.RemainingAmount);
        var activeDebts = list.Where(d => !d.IsCompleted && d.RemainingAmount > 0).ToList();
        var monthlyInstallments = activeDebts.Sum(d => d.InstallmentAmount);

        TxtTotalDebtSum.Text = $"₺{totalDebt:N2}";
        TxtRemainingDebtSum.Text = $"₺{remainingDebt:N2}";
        TxtInstallmentSum.Text = $"₺{monthlyInstallments:N2}";
        TxtActiveDebtCount.Text = $"{activeDebts.Count} Adet";
    }

    private async void RefreshAll_Click(object sender, RoutedEventArgs e)
    {
        await LoadAllDataAsync();
    }

    private async void AddExpense_Click(object sender, RoutedEventArgs e)
    {
        var win = new GeneralExpenseEditWindow(null, _reminderService)
        {
            Owner = this
        };
        if (win.ShowDialog() == true)
        {
            await LoadExpensesAsync();
        }
    }

    private async void EditExpense_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is GeneralExpense expense)
        {
            var win = new GeneralExpenseEditWindow(expense, _reminderService)
            {
                Owner = this
            };
            if (win.ShowDialog() == true)
            {
                await LoadExpensesAsync();
            }
        }
    }

    private async void DeleteExpense_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is GeneralExpense expense)
        {
            var res = MessageBox.Show(
                $"'{expense.Title}' isimli genel gideri silmek istediğinize emin misiniz?\n\n(Bu işlemden sonra yeni ayların takvimine eklenmeyecektir.)",
                "Gider Silme Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                await _reminderService.DeleteGeneralExpenseAsync(expense.Id);
                await LoadExpensesAsync();
            }
        }
    }

    private async void ResetExpenses_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            "Varsayılan genel gider şablonları (Kira, Elektrik/Su, İnternet, SGK, Vergi vb.) yeniden yüklensin mi?",
            "Şablonları Yenile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            await _reminderService.ResetDefaultGeneralExpensesAsync();
            await LoadExpensesAsync();
        }
    }

    private async void AddDebt_Click(object sender, RoutedEventArgs e)
    {
        var win = new DebtEditWindow(null, _reminderService)
        {
            Owner = this
        };
        if (win.ShowDialog() == true)
        {
            await LoadDebtsAsync();
        }
    }

    private async void EditDebt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FinancialDebt debt)
        {
            var win = new DebtEditWindow(debt, _reminderService)
            {
                Owner = this
            };
            if (win.ShowDialog() == true)
            {
                await LoadDebtsAsync();
            }
        }
    }

    private async void DeleteDebt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FinancialDebt debt)
        {
            var res = MessageBox.Show(
                $"'{debt.Title}' isimli borç / kredi kaydını silmek istediğinize emin misiniz?",
                "Borç Silme Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                await _reminderService.DeleteDebtAsync(debt.Id);
                await LoadDebtsAsync();
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
