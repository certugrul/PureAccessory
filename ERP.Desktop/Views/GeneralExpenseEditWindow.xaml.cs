using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class GeneralExpenseEditWindow : Window
{
    private readonly GeneralExpense? _existing;
    private readonly ReminderService _reminderService;

    public GeneralExpenseEditWindow(GeneralExpense? existing, ReminderService reminderService)
    {
        InitializeComponent();
        _existing = existing;
        _reminderService = reminderService;

        if (_existing != null)
        {
            TxtHeading.Text = "✏️ Genel Gideri Düzenle";
            TxtTitle.Text = _existing.Title;
            TxtAmount.Text = NumberParser.FormatForEditing(_existing.MonthlyAmount);
            TxtDayOfMonth.Text = _existing.DayOfMonth.ToString();
            TxtDesc.Text = _existing.Description ?? "";

            foreach (ComboBoxItem item in CmbCategory.Items)
            {
                if (item.Content.ToString() == _existing.Category)
                {
                    CmbCategory.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TxtTitle.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show("Lütfen gider / masraf adını giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtTitle.Focus();
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtAmount.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Lütfen geçerli pozitif bir aylık tutar giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAmount.Focus();
            return;
        }

        if (!int.TryParse(TxtDayOfMonth.Text.Trim(), out int day) || day < 1 || day > 31)
        {
            MessageBox.Show("Lütfen 1 ile 31 arasında geçerli bir gün giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtDayOfMonth.Focus();
            return;
        }

        var category = (CmbCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Diğer";
        var desc = TxtDesc.Text.Trim();

        var item = _existing ?? new GeneralExpense();
        item.Title = title;
        item.Category = category;
        item.MonthlyAmount = amount;
        item.DayOfMonth = day;
        item.Description = desc;

        try
        {
            await _reminderService.SaveOrUpdateGeneralExpenseAsync(item);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
