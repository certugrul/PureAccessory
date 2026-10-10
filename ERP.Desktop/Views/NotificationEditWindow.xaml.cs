using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class NotificationEditWindow : Window
{
    private readonly FinancialReminder? _existing;
    private readonly ReminderService _reminderService;

    public NotificationEditWindow(FinancialReminder? existing, ReminderService reminderService, DateTime? defaultDate = null)
    {
        InitializeComponent();
        _existing = existing;
        _reminderService = reminderService;

        DpDueDate.SelectedDate = defaultDate ?? DateTime.Today;

        if (_existing != null)
        {
            TxtHeading.Text = "✏️ Hatırlatmayı Düzenle";
            TxtTitle.Text = _existing.Title;
            TxtAmount.Text = NumberParser.FormatForEditing(_existing.Amount);
            DpDueDate.SelectedDate = _existing.DueDate;
            ChkRecurring.IsChecked = _existing.IsRecurringMonthly;
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
            MessageBox.Show("Lütfen başlık / ödeme adı giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtTitle.Focus();
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtAmount.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Lütfen geçerli pozitif bir tutar giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAmount.Focus();
            return;
        }

        var category = (CmbCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Diğer";
        var selectedDate = DpDueDate.SelectedDate ?? DateTime.Today;
        int originalDay = selectedDate.Day;
        var dueDate = selectedDate;
        if (dueDate.DayOfWeek == DayOfWeek.Saturday)
        {
            dueDate = dueDate.AddDays(2);
        }
        else if (dueDate.DayOfWeek == DayOfWeek.Sunday)
        {
            dueDate = dueDate.AddDays(1);
        }

        bool recurring = ChkRecurring.IsChecked == true;
        var desc = TxtDesc.Text.Trim();

        var item = _existing ?? new FinancialReminder();
        item.Title = title;
        item.Category = category;
        item.Amount = amount;
        item.DueDate = dueDate;
        item.DayOfMonth = originalDay;
        item.IsRecurringMonthly = recurring;
        item.Description = desc;

        try
        {
            await _reminderService.SaveOrUpdateReminderAsync(item);
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
