using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public class CalendarDayItem
{
    public int DayNumber { get; set; }
    public DateTime Date { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsToday { get; set; }
    public bool IsSelected { get; set; }
    public int ReminderCount { get; set; }
    public int PendingCount { get; set; }
    public int PaidCount { get; set; }
    public decimal TotalPendingAmount { get; set; }

    public Brush BackgroundBrush { get; set; } = Brushes.White;
    public Brush BorderBrush { get; set; } = Brushes.Transparent;
    public Thickness BorderThickness { get; set; } = new Thickness(1);
    public FontWeight DayFontWeight { get; set; } = FontWeights.Normal;
    public Brush DayForegroundBrush { get; set; } = Brushes.Black;

    public Visibility BadgeVisibility => ReminderCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Brush BadgeBackgroundBrush { get; set; } = Brushes.Transparent;
    public Brush BadgeForegroundBrush { get; set; } = Brushes.White;
    public string BadgeText { get; set; } = "";
}

public partial class DashboardView : Page
{
    private readonly ClosureService _closureService;
    private readonly WarehouseService _warehouseService;
    private readonly ReminderService _reminderService;
    private readonly DispatcherTimer _clockTimer;
    private readonly CultureInfo _turkishCulture = new("tr-TR");

    private DateTime _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selectedDay = DateTime.Today;
    private List<FinancialReminder> _allMonthReminders = new();
    private readonly ObservableCollection<CalendarDayItem> _calendarDays = new();

    public DashboardView()
    {
        InitializeComponent();
        _closureService = new ClosureService();
        _warehouseService = new WarehouseService();
        _reminderService = new ReminderService();

        CalendarDaysItemsControl.ItemsSource = _calendarDays;

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();
        UpdateClock();

        Loaded += DashboardView_Loaded;
    }

    private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        await UpdateDashboardCardsAsync();
        await LoadRemindersAndBuildCalendarAsync();
    }

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        UpdateClock();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        LiveTimeText.Text = now.ToString("HH:mm:ss");
        LiveDateText.Text = now.ToString("dd MMMM yyyy dddd", _turkishCulture);
    }

    private async Task LoadRemindersAndBuildCalendarAsync()
    {
        try
        {
            TxtMonthYearHeader.Text = _currentMonth.ToString("MMMM yyyy", _turkishCulture);

            _allMonthReminders = await _reminderService.GetRemindersForMonthAsync(_currentMonth.Year, _currentMonth.Month)
                                 ?? new List<FinancialReminder>();

            // Ay toplamları
            var total = _allMonthReminders.Sum(r => r.Amount);
            var paid = _allMonthReminders.Where(r => r.IsPaid).Sum(r => r.Amount);
            var pending = _allMonthReminders.Where(r => !r.IsPaid).Sum(r => r.Amount);
            var pendingCount = _allMonthReminders.Count(r => !r.IsPaid);

            TxtMonthTotal.Text = $"₺{total:N2}";
            TxtMonthPaid.Text = $"₺{paid:N2}";
            TxtMonthPending.Text = $"₺{pending:N2}";
            TxtPendingBadge.Text = $"{pendingCount} Bekleyen Ödeme";

            BuildCalendarMatrix();
            UpdateSelectedDayList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardView] Takvim yüklenirken hata: {ex.Message}");
            _allMonthReminders = new List<FinancialReminder>();
            BuildCalendarMatrix();
        }
    }

    private void BuildCalendarMatrix()
    {
        _calendarDays.Clear();

        var firstDayOfMonth = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
        int daysInMonth = DateTime.DaysInMonth(_currentMonth.Year, _currentMonth.Month);

        // Pazartesi = 0, Pazar = 6
        int startDayOfWeek = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;

        // Önceki aydan kalan günleri doldur
        var prevMonth = firstDayOfMonth.AddMonths(-1);
        int daysInPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);

        for (int i = startDayOfWeek - 1; i >= 0; i--)
        {
            int dayNum = daysInPrevMonth - i;
            var dt = new DateTime(prevMonth.Year, prevMonth.Month, dayNum);
            _calendarDays.Add(CreateDayItem(dt, isCurrentMonth: false));
        }

        // Bu ayın günleri
        for (int day = 1; day <= daysInMonth; day++)
        {
            var dt = new DateTime(_currentMonth.Year, _currentMonth.Month, day);
            _calendarDays.Add(CreateDayItem(dt, isCurrentMonth: true));
        }

        // Kalan günleri sonraki aydan tamamla (Toplam 35 veya 42 hücre)
        int totalCells = _calendarDays.Count <= 35 ? 35 : 42;
        var nextMonth = firstDayOfMonth.AddMonths(1);
        int nextDayNum = 1;
        while (_calendarDays.Count < totalCells)
        {
            var dt = new DateTime(nextMonth.Year, nextMonth.Month, nextDayNum++);
            _calendarDays.Add(CreateDayItem(dt, isCurrentMonth: false));
        }
    }

    private CalendarDayItem CreateDayItem(DateTime date, bool isCurrentMonth)
    {
        bool isToday = date.Date == DateTime.Today;
        bool isSelected = date.Date == _selectedDay.Date;

        var dayReminders = _allMonthReminders.Where(r => r.DueDate.Date == date.Date).ToList();
        int count = dayReminders.Count;
        int pendingCount = dayReminders.Count(r => !r.IsPaid);
        int paidCount = dayReminders.Count(r => r.IsPaid);
        decimal pendingTotal = dayReminders.Where(r => !r.IsPaid).Sum(r => r.Amount);

        var item = new CalendarDayItem
        {
            DayNumber = date.Day,
            Date = date,
            IsCurrentMonth = isCurrentMonth,
            IsToday = isToday,
            IsSelected = isSelected,
            ReminderCount = count,
            PendingCount = pendingCount,
            PaidCount = paidCount,
            TotalPendingAmount = pendingTotal
        };

        // Görsel stiller
        if (!isCurrentMonth)
        {
            item.BackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
            item.DayForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            item.BorderBrush = Brushes.Transparent;
        }
        else if (isSelected)
        {
            item.BackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
            item.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            item.BorderThickness = new Thickness(2);
            item.DayForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF"));
            item.DayFontWeight = FontWeights.Bold;
        }
        else if (isToday)
        {
            item.BackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0FDF4"));
            item.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86EFAC"));
            item.BorderThickness = new Thickness(1.5);
            item.DayForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15803D"));
            item.DayFontWeight = FontWeights.Bold;
        }
        else
        {
            item.BackgroundBrush = Brushes.White;
            item.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
            item.DayForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
        }

        // Bildirim Rozeti
        if (count > 0)
        {
            if (pendingCount > 0)
            {
                item.BadgeBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                item.BadgeForegroundBrush = Brushes.White;
                item.BadgeText = pendingTotal >= 1000 ? $"₺{pendingTotal / 1000:N0}K" : $"₺{pendingTotal:N0}";
            }
            else
            {
                item.BadgeBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                item.BadgeForegroundBrush = Brushes.White;
                item.BadgeText = "✓ Ödendi";
            }
        }

        return item;
    }

    private void UpdateSelectedDayList()
    {
        bool showAllMonth = RbFilterAllMonth.IsChecked == true;

        List<FinancialReminder> filtered;
        if (showAllMonth)
        {
            TxtSelectedDayTitle.Text = $"🗓 {TxtMonthYearHeader.Text} — Tüm Ödemeler";
            filtered = _allMonthReminders.OrderBy(r => r.DueDate).ToList();
            int pending = filtered.Count(r => !r.IsPaid);
            TxtSelectedDaySubtitle.Text = $"Toplam {filtered.Count} adet ödeme ({pending} bekleyen)";
        }
        else
        {
            TxtSelectedDayTitle.Text = $"🗓 {_selectedDay.ToString("dd MMMM yyyy dddd", _turkishCulture)}";
            filtered = _allMonthReminders.Where(r => r.DueDate.Date == _selectedDay.Date).OrderBy(r => r.IsPaid).ToList();
            int pending = filtered.Count(r => !r.IsPaid);
            TxtSelectedDaySubtitle.Text = filtered.Count > 0
                ? $"Bu güne ait {filtered.Count} ödeme ({pending} bekliyor)"
                : "Bu güne ait planlanmış ödeme bulunmuyor";
        }

        RemindersItemsControl.ItemsSource = filtered;

        if (filtered.Count == 0)
        {
            PnlEmptyState.Visibility = Visibility.Visible;
            RemindersScrollViewer.Visibility = Visibility.Collapsed;
        }
        else
        {
            PnlEmptyState.Visibility = Visibility.Collapsed;
            RemindersScrollViewer.Visibility = Visibility.Visible;
        }
    }

    private void DayCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CalendarDayItem item)
        {
            _selectedDay = item.Date;
            if (item.Date.Month != _currentMonth.Month)
            {
                _currentMonth = new DateTime(item.Date.Year, item.Date.Month, 1);
            }
            BuildCalendarMatrix();
            UpdateSelectedDayList();
        }
    }

    private async void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(-1);
        _selectedDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
        await LoadRemindersAndBuildCalendarAsync();
    }

    private async void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(1);
        _selectedDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
        await LoadRemindersAndBuildCalendarAsync();
    }

    private async void Today_Click(object sender, RoutedEventArgs e)
    {
        _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _selectedDay = DateTime.Today;
        await LoadRemindersAndBuildCalendarAsync();
    }

    private void FilterMode_Changed(object sender, RoutedEventArgs e)
    {
        if (RemindersItemsControl != null)
        {
            UpdateSelectedDayList();
        }
    }

    private async void RefreshCalendar_Click(object sender, RoutedEventArgs e)
    {
        await LoadRemindersAndBuildCalendarAsync();
        await UpdateDashboardCardsAsync();
    }

    private async void AddReminder_Click(object sender, RoutedEventArgs e)
    {
        var win = new NotificationEditWindow(null, _reminderService, _selectedDay)
        {
            Owner = Window.GetWindow(this)
        };
        if (win.ShowDialog() == true)
        {
            await LoadRemindersAndBuildCalendarAsync();
        }
    }

    private async void AddReminderForSelectedDay_Click(object sender, RoutedEventArgs e)
    {
        AddReminder_Click(sender, e);
    }

    private async void EditReminder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FinancialReminder reminder)
        {
            var win = new NotificationEditWindow(reminder, _reminderService, reminder.DueDate)
            {
                Owner = Window.GetWindow(this)
            };
            if (win.ShowDialog() == true)
            {
                await LoadRemindersAndBuildCalendarAsync();
            }
        }
    }

    private async void DeleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FinancialReminder reminder)
        {
            if (reminder.GeneralExpenseId.HasValue)
            {
                var result = MessageBox.Show(
                    $"'{reminder.Title}' bir düzenli genel gider şablonuna bağlıdır.\n\n" +
                    "Bu gideri sistemden tamamen silmek mi istersiniz, yoksa yalnızca bu ayın takviminden mi kaldırmak istersiniz?\n\n" +
                    "• [Evet] = Şablonu tamamen sil (Gelecek aylarda da bir daha çıkmaz)\n" +
                    "• [Hayır] = Sadece bu ayın takviminden sil\n" +
                    "• [İptal] = Vazgeç",
                    "Genel Gider Silme Seçeneği",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _reminderService.DeleteGeneralExpenseAsync(reminder.GeneralExpenseId.Value);
                    await LoadRemindersAndBuildCalendarAsync();
                }
                else if (result == MessageBoxResult.No)
                {
                    await _reminderService.DeleteReminderAsync(reminder.Id);
                    await LoadRemindersAndBuildCalendarAsync();
                }
            }
            else if (reminder.DebtId.HasValue)
            {
                var result = MessageBox.Show(
                    $"'{reminder.Title}' bir vadeli borç / kredi taksitine bağlıdır.\n\n" +
                    "Bu borcu/krediyi sistemden tamamen silmek mi istersiniz, yoksa yalnızca bu ayın takviminden mi kaldırmak istersiniz?\n\n" +
                    "• [Evet] = Borç kaydını tamamen sil (Hiçbir ayda çıkmaz)\n" +
                    "• [Hayır] = Sadece bu ayın takviminden sil\n" +
                    "• [İptal] = Vazgeç",
                    "Borç Silme Seçeneği",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _reminderService.DeleteDebtAsync(reminder.DebtId.Value);
                    await LoadRemindersAndBuildCalendarAsync();
                }
                else if (result == MessageBoxResult.No)
                {
                    await _reminderService.DeleteReminderAsync(reminder.Id);
                    await LoadRemindersAndBuildCalendarAsync();
                }
            }
            else
            {
                var res = MessageBox.Show(
                    $"'{reminder.Title}' isimli hatırlatma kaydını silmek istediğinize emin misiniz?",
                    "Silme Onayı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    await _reminderService.DeleteReminderAsync(reminder.Id);
                    await LoadRemindersAndBuildCalendarAsync();
                }
            }
        }
    }

    private async void MarkPaid_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FinancialReminder reminder)
        {
            var win = new ReminderPaymentWindow(reminder, _reminderService)
            {
                Owner = Window.GetWindow(this)
            };
            if (win.ShowDialog() == true)
            {
                await LoadRemindersAndBuildCalendarAsync();
                await UpdateDashboardCardsAsync();
            }
        }
    }

    private async void ManageTemplates_Click(object sender, RoutedEventArgs e)
    {
        var win = new FinancialTemplatesWindow(_reminderService)
        {
            Owner = Window.GetWindow(this)
        };
        if (win.ShowDialog() == true)
        {
            await _reminderService.SyncMonthRemindersAsync(_currentMonth.Year, _currentMonth.Month);
            await LoadRemindersAndBuildCalendarAsync();
            await UpdateDashboardCardsAsync();
        }
    }

    private async Task UpdateDashboardCardsAsync()
    {
        try
        {
            var summary = await _closureService.GetActiveDashboardMetricsAsync(DateTime.Today);
            CardRevenueText.Text = $"₺{summary.TotalRevenue:N2}";
            CardProfitText.Text = $"₺{summary.TotalProfit:N2}";

            var closures = await _closureService.GetClosuresAsync();
            var lastClosure = closures
                .Where(c => c.ClosureDate.Date == DateTime.Today)
                .OrderByDescending(c => c.ClosedAt)
                .ThenByDescending(c => c.Id)
                .FirstOrDefault();

            var cashSummary = await new CashService().GetCashSummaryAsync(DateTime.Today);
            if (cashSummary.IsOpen)
            {
                CardCashText.Text = cashSummary.FormattedCurrentBalance;
                CardCashText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                CardCashSubText.Text = $"🟢 Gün Açık (Kasa: {cashSummary.FormattedOpeningCash})";
                CardCashSubText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
            }
            else
            {
                CardCashText.Text = "₺0,00";
                CardCashText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                CardCashSubText.Text = "🔴 Gün Kapalı (Kasa Açılışı Bekliyor)";
                CardCashSubText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            }

            if (lastClosure != null && summary.TotalRevenue == 0)
            {
                CardRevenueSubText.Text = $"Gün bitişi yapıldı ({lastClosure.ClosedAt:HH:mm}) - Sıfırlandı";
                CardRevenueSubText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            }
            else if (summary.TotalRevenue > 0)
            {
                CardRevenueSubText.Text = $"{summary.SalesCount} Fiş (Kapanış Bekleyen)";
                CardRevenueSubText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else
            {
                CardRevenueSubText.Text = "Henüz satış kaydedilmedi";
                CardRevenueSubText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
            }

            CardStockText.Text = $"₺{summary.TotalCreditCard:N2}";
            CardStockSubText.Text = "Bugünkü kredi kartı & POS";
        }
        catch
        {
            // Sessizce devam et
        }
    }

    private async void RefreshDashboard_Click(object sender, RoutedEventArgs e)
    {
        await UpdateDashboardCardsAsync();
        await LoadRemindersAndBuildCalendarAsync();
    }

    private void OpenDebts_Click(object sender, RoutedEventArgs e)
    {
        NavigationService?.Navigate(new DebtsView());
    }
}
