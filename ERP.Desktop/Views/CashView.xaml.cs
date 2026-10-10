using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class CashView : Page
{
    private readonly CashService _cashService;
    public ObservableCollection<CashTransaction> Transactions { get; } = new();

    public CashView()
    {
        InitializeComponent();
        _cashService = new CashService();
        LedgerGrid.ItemsSource = Transactions;

        // Önbellekteki kasa durumunu anında ekrana yansıt (beklemesiz)
        if (AppDataCache.IsLoaded && AppDataCache.TodayCashSummary != null)
        {
            var summary = AppDataCache.TodayCashSummary;
            CardOpeningText.Text = summary.FormattedOpeningCash;
            CardCashSalesText.Text = summary.FormattedCashSales;
            CardManualText.Text = $"₺{(summary.CashIn - summary.CashOut):N2}";
            CardTotalBalanceText.Text = summary.FormattedCurrentBalance;

            var session = AppDataCache.TodayCashSession;
            if (session != null && session.Status == "Açık")
            {
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
                StatusText.Text = $"🟢 Gün Açık & Kasa Açık (Saat: {session.FormattedOpenedAt} | Başlangıç: {session.FormattedOpeningCash})";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                OpeningTitleText.Text = "💵 Gün Açık (Kasa Oturumu Aktif)";
                BtnOpenRegister.Content = "✏️ Başlangıç Nakitini Güncelle";
                BtnCloseRegister.Visibility = Visibility.Visible;
                TxtOpeningCash.Text = session.OpeningCash.ToString("N2", CultureInfo.CurrentCulture);
            }
            else
            {
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                StatusText.Text = "🔴 Gün Kapalı / Kasa Kapalı (Açılış Bekliyor)";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                OpeningTitleText.Text = "💵 Gün Açılışı & Kasa Devir Nakiti";
                BtnOpenRegister.Content = "🌅 Gün Açılışı Yap (Kasayı Aç)";
                BtnCloseRegister.Visibility = Visibility.Collapsed;
                TxtOpeningCash.Text = NumberParser.FormatForEditing(AppDataCache.CentralCashBalance);
            }
        }

        Loaded += CashView_Loaded;
    }

    private async void CashView_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshCashDataAsync();
    }

    private async Task RefreshCashDataAsync()
    {
        try
        {
            var summary = await _cashService.GetCashSummaryAsync(DateTime.Today);
            var session = await _cashService.GetTodaySessionAsync(DateTime.Today);

            // Metrik kartlarını doldur
            CardOpeningText.Text = summary.FormattedOpeningCash;
            CardCashSalesText.Text = summary.FormattedCashSales;
            CardManualText.Text = $"₺{(summary.CashIn - summary.CashOut):N2}";
            CardTotalBalanceText.Text = summary.FormattedCurrentBalance;

            // Kasa Durumu
            if (session != null && session.Status == "Açık")
            {
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
                StatusText.Text = $"🟢 Gün Açık & Kasa Açık (Saat: {session.FormattedOpenedAt} | Başlangıç: {session.FormattedOpeningCash})";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));

                OpeningTitleText.Text = "💵 Gün Açık (Kasa Oturumu Aktif)";
                BtnOpenRegister.Content = "✏️ Başlangıç Nakitini Güncelle";
                BtnCloseRegister.Visibility = Visibility.Visible;
                TxtOpeningCash.Text = session.OpeningCash.ToString("N2", CultureInfo.CurrentCulture);
            }
            else
            {
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                StatusText.Text = "🔴 Gün Kapalı / Kasa Kapalı (Açılış Bekliyor)";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));

                OpeningTitleText.Text = "💵 Gün Açılışı & Kasa Devir Nakiti";
                BtnOpenRegister.Content = "🌅 Gün Açılışı Yap (Kasayı Aç)";
                BtnCloseRegister.Visibility = Visibility.Collapsed;
                
                // Kasadaki toplam nakiti öner (dinamik)
                decimal dynamicOpening = await _cashService.GetTotalCashBalanceAsync();
                TxtOpeningCash.Text = NumberParser.FormatForEditing(dynamicOpening);
            }

            // Hareket dökümünü doldur
            var ledger = await _cashService.GetDayLedgerAsync(DateTime.Today);
            Transactions.Clear();
            foreach (var item in ledger)
            {
                Transactions.Add(item);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kasa verileri yüklenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PresetCash_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && NumberParser.TryParseDecimal(btn.Tag?.ToString(), out var addVal))
        {
            var current = NumberParser.ParseDecimalSafe(TxtOpeningCash.Text);
            TxtOpeningCash.Text = NumberParser.FormatForEditing(current + addVal);
        }
    }

    private async void OpenRegister_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanTakeClosure && !AuthService.IsAdmin)
        {
            MessageBox.Show("Kasa açılış devri belirleme yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!NumberParser.TryParseDecimal(TxtOpeningCash.Text, out var openingCash))
        {
            MessageBox.Show("Lütfen geçerli bir başlangıç nakit tutarı girin.", "Geçersiz Tutar", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtOpeningCash.Focus();
            return;
        }

        if (openingCash < 0)
        {
            MessageBox.Show("Başlangıç nakiti negatif olamaz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var session = await _cashService.OpenCashRegisterAsync(openingCash, "Kullanıcı tarafından açıldı");

        MessageBox.Show($"Kasa gün açılışı başarıyla yapıldı!\n\n• Devreden Açılış Nakiti: ₺{openingCash:N2}\n• Açılış Saati: {session.FormattedOpenedAt}\n\nSatış terminali kullanıma hazırdır.", "Gün Açılışı Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);

        await RefreshCashDataAsync();
    }

    private async void AddCashIn_Click(object sender, RoutedEventArgs e)
    {
        // Basit nakit giriş diyaloğu
        var amountStr = Microsoft.VisualBasic.Interaction.InputBox("Kasaya eklenecek nakit tutarını girin (₺):", "Kasaya Nakit Ekle", "200");
        if (string.IsNullOrWhiteSpace(amountStr)) return;

        if (NumberParser.TryParseDecimal(amountStr, out var amount) && amount > 0)
        {
            var desc = Microsoft.VisualBasic.Interaction.InputBox("Giriş açıklaması (Örn: Bozuk para takviyesi):", "Açıklama", "Nakit Takviye");
            await _cashService.AddTransactionAsync("Nakit Giriş", amount, string.IsNullOrWhiteSpace(desc) ? "Kasaya Nakit Ekleme" : desc);
            await RefreshCashDataAsync();
            MessageBox.Show($"Kasaya ₺{amount:N2} nakit girişi işlendi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Geçerli bir tutar girmediniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void AddCashOut_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanAddCashExpense && !AuthService.IsAdmin)
        {
            MessageBox.Show("Kasadan masraf veya nakit çıkış girme yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var amountStr = Microsoft.VisualBasic.Interaction.InputBox("Kasadan çıkacak nakit masraf tutarını girin (₺):", "Kasadan Nakit Çıkar / Masraf", "100");
        if (string.IsNullOrWhiteSpace(amountStr)) return;

        if (!NumberParser.TryParseDecimal(amountStr, out var amount) || amount <= 0)
        {
            MessageBox.Show("Geçerli bir pozitif tutar girmediniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 4. Kural: Kasadaki nakit girilen masraftan küçükse uyarı ver
        decimal currentCash = await _cashService.GetTotalCashBalanceAsync();
        if (amount > currentCash)
        {
            var conf = MessageBox.Show(
                $"DİKKAT: Kasadaki nakit tutar yetersiz!\n\n" +
                $"• Kasadaki Mevcut Nakit : ₺{currentCash:N2}\n" +
                $"• Girilen Masraf Tutarı  : ₺{amount:N2}\n" +
                $"• Kasa Açığı             : -₺{(amount - currentCash):N2}\n\n" +
                "Kasadaki nakit tutarı girilen masraftan küçüktür. Yine de masrafı kaydetmek istiyor musunuz?",
                "Kasadaki Tutar Yetersiz",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (conf != MessageBoxResult.Yes) return;
        }

        var desc = Microsoft.VisualBasic.Interaction.InputBox("Çıkış / Masraf açıklaması (Örn: Çay/Temizlik, Yemek, Kırtasiye):", "Açıklama", "Günlük Masraf");
        string finalDesc = string.IsNullOrWhiteSpace(desc) ? "Kasadan Nakit Masraf Çıkışı" : desc.Trim();

        await _cashService.AddTransactionAsync("Nakit Çıkış", amount, finalDesc);
        await _cashService.RecordExpenseAsync(amount, finalDesc, DateTime.Now);
        await RefreshCashDataAsync();
        MessageBox.Show($"Kasadan ₺{amount:N2} nakit masraf çıkışı işlendi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void CloseRegister_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanTakeClosure && !AuthService.IsAdmin)
        {
            MessageBox.Show("Gün bitişi ve kasa kapatma yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var summary = await _cashService.GetCashSummaryAsync(DateTime.Today);
        var msg = $"Gün bitişi yapıp kasayı kapatmak istediğinize emin misiniz?\n\n" +
                  $"• Başlangıç Nakiti : {summary.FormattedOpeningCash}\n" +
                  $"• Nakit Satışlar   : {summary.FormattedCashSales}\n" +
                  $"• Manuel Net Giriş : ₺{(summary.CashIn - summary.CashOut):N2}\n" +
                  $"• Kasadaki Toplam  : {summary.FormattedCurrentBalance}\n\n" +
                  $"Kasa kapatıldığında gün bitişi gerçekleşecek ve yeni gün açılışı yapılana kadar satış yapılamayacaktır.";

        var res = MessageBox.Show(msg, "Gün Bitişi & Kasa Kapatma Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            await _cashService.CloseTodaySessionAsync(DateTime.Today, DateTime.Now);
            MessageBox.Show("Gün bitişi yapıldı ve kasa kapatıldı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshCashDataAsync();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCashDataAsync();
    }
}
