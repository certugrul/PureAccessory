using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class SettingsWindow : Window
{
    private string _selectedPrimaryColor = "#2563EB";
    private string _selectedSidebarColor = "#111827";
    private string _selectedModeOption = "Açık";
    private string _selectedFontFamily = "Segoe UI";

    public SettingsWindow()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var settings = ThemeService.Current;
        TxtStoreName.Text = settings.StoreName;
        TxtSubtitle.Text = settings.StoreSubtitle;
        _selectedPrimaryColor = settings.PrimaryColor;
        _selectedSidebarColor = settings.SidebarColor;
        _selectedModeOption = settings.DarkModeOption ?? "Açık";
        _selectedFontFamily = settings.AppFontFamily ?? "Segoe UI";

        ChkSound.IsChecked = settings.SoundEffectsEnabled;
        ChkAutoPrint.IsChecked = settings.AutoPrintReceipt;
        ChkCompactMode.IsChecked = settings.CompactMode;

        TxtReceiptHeader.Text = settings.ReceiptStoreHeader ?? settings.StoreName;
        TxtReceiptFooter.Text = settings.ReceiptFooterNote ?? "Bizi tercih ettiğiniz için teşekkür ederiz!";
        DpCashStartDate.SelectedDate = settings.CashBalanceStartDate ?? new DateTime(2026, 10, 8);

        TxtAppVersionLabel.Text = $"Mevcut Sürüm: v{UpdateService.CurrentVersion}";
        TxtUpdateUrl.Text = settings.UpdateCheckUrl ?? "";
        ChkAutoUpdate.IsChecked = settings.AutoCheckUpdates;
        TxtCustomDbConn.Text = settings.CustomDbConnectionString ?? "";

        ChkAutoSyncOnClosure.IsChecked = settings.AutoSyncOnClosure;
        TxtAutoSyncTargetDir.Text = settings.AutoSyncTargetDirectory ?? "";

        HighlightSelectedColor(_selectedPrimaryColor);

        // Sidebar tonunu seç
        if (_selectedSidebarColor == "#0F172A")
        {
            RbSidebarNavy.IsChecked = true;
        }
        else if (_selectedSidebarColor == "#1E293B")
        {
            RbSidebarSlate.IsChecked = true;
        }
        else
        {
            RbSidebarDark.IsChecked = true;
        }

        // Görünüm Modunu Seç
        if (_selectedModeOption == "Koyu")
        {
            RbModeDark.IsChecked = true;
        }
        else if (_selectedModeOption == "Yumuşak Gri")
        {
            RbModeSoft.IsChecked = true;
        }
        else
        {
            RbModeLight.IsChecked = true;
        }

        // Fontu Seç
        foreach (ComboBoxItem item in CmbFontFamily.Items)
        {
            if (item.Tag?.ToString() == _selectedFontFamily)
            {
                CmbFontFamily.SelectedItem = item;
                break;
            }
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SelectPrimaryColor_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string hex)
        {
            _selectedPrimaryColor = hex;
            HighlightSelectedColor(hex);

            // Anlık canlı önizleme uygula
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                Application.Current.Resources["PrimaryBrush"] = new SolidColorBrush(color);

                var lightColor = Color.FromArgb(35, color.R, color.G, color.B);
                Application.Current.Resources["PrimaryLightBrush"] = new SolidColorBrush(lightColor);

                byte rH = (byte)Math.Max(0, color.R - 25);
                byte gH = (byte)Math.Max(0, color.G - 25);
                byte bH = (byte)Math.Max(0, color.B - 25);
                Application.Current.Resources["PrimaryHoverBrush"] = new SolidColorBrush(Color.FromRgb(rH, gH, bH));
            }
            catch { }
        }
    }

    private void HighlightSelectedColor(string hex)
    {
        string colorName = "Özel Renk";
        foreach (var child in PanelColors.Children)
        {
            if (child is Border b && b.Tag is string tagHex)
            {
                if (string.Equals(tagHex, hex, StringComparison.OrdinalIgnoreCase))
                {
                    b.BorderBrush = new SolidColorBrush(Colors.Black);
                    b.BorderThickness = new Thickness(3);
                    colorName = b.ToolTip?.ToString() ?? "Seçili Renk";
                }
                else
                {
                    b.BorderBrush = Brushes.Transparent;
                    b.BorderThickness = new Thickness(1);
                }
            }
        }

        if (TxtSelectedColorName != null)
        {
            TxtSelectedColorName.Text = colorName;
            try
            {
                TxtSelectedColorName.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch { }
        }
    }

    private void SidebarColor_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string hex)
        {
            _selectedSidebarColor = hex;

            // Anlık canlı önizleme uygula
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                Application.Current.Resources["SidebarBrush"] = new SolidColorBrush(color);
            }
            catch { }
        }
    }

    private void ModeOption_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string mode)
        {
            _selectedModeOption = mode;

            // Anlık canlı mod değişimi
            try
            {
                if (mode == "Koyu")
                {
                    Application.Current.Resources["PageBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    Application.Current.Resources["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                }
                else if (mode == "Yumuşak Gri")
                {
                    Application.Current.Resources["PageBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                    Application.Current.Resources["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                }
                else
                {
                    Application.Current.Resources["PageBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                    Application.Current.Resources["CardBrush"] = Brushes.White;
                }
            }
            catch { }
        }
    }

    private void FontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbFontFamily.SelectedItem is ComboBoxItem item && item.Tag is string fontName)
        {
            _selectedFontFamily = fontName;
            try
            {
                Application.Current.Resources["AppFontFamily"] = new FontFamily(fontName);
            }
            catch { }
        }
    }

    private void TestSound_Click(object sender, RoutedEventArgs e)
    {
        if (ChkSound.IsChecked == true)
        {
            SoundService.PlaySuccess();
            MessageBox.Show("🔔 Ses efekti başarıyla çalındı!", "Ses Testi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Ses efektleri kutusu işaretli olmadığı için ses çalınmadı.", "Ses Kapalı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetDefaultCashDate_Click(object sender, RoutedEventArgs e)
    {
        DpCashStartDate.SelectedDate = new DateTime(2026, 10, 8);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            StoreName = string.IsNullOrWhiteSpace(TxtStoreName.Text) ? "Pure Accessory Otomasyon" : TxtStoreName.Text.Trim(),
            StoreSubtitle = string.IsNullOrWhiteSpace(TxtSubtitle.Text) ? "Satış & Finans Yönetim Sistemi" : TxtSubtitle.Text.Trim(),
            PrimaryColor = _selectedPrimaryColor,
            SidebarColor = _selectedSidebarColor,
            DarkModeOption = _selectedModeOption,
            AppFontFamily = _selectedFontFamily,
            SoundEffectsEnabled = ChkSound.IsChecked ?? true,
            AutoPrintReceipt = ChkAutoPrint.IsChecked ?? false,
            CompactMode = ChkCompactMode.IsChecked ?? false,
            ReceiptStoreHeader = string.IsNullOrWhiteSpace(TxtReceiptHeader.Text) ? TxtStoreName.Text.Trim() : TxtReceiptHeader.Text.Trim(),
            ReceiptFooterNote = string.IsNullOrWhiteSpace(TxtReceiptFooter.Text) ? "Bizi tercih ettiğiniz için teşekkür ederiz!" : TxtReceiptFooter.Text.Trim(),
            CashBalanceStartDate = DpCashStartDate.SelectedDate ?? new DateTime(2026, 10, 8),
            UpdateCheckUrl = TxtUpdateUrl.Text.Trim(),
            AutoCheckUpdates = ChkAutoUpdate.IsChecked ?? true,
            CustomDbConnectionString = string.IsNullOrWhiteSpace(TxtCustomDbConn.Text) ? null : TxtCustomDbConn.Text.Trim(),
            AutoSyncOnClosure = ChkAutoSyncOnClosure.IsChecked ?? true,
            AutoSyncTargetDirectory = string.IsNullOrWhiteSpace(TxtAutoSyncTargetDir.Text) ? null : TxtAutoSyncTargetDir.Text.Trim()
        };

        ThemeService.Save(settings);
        MessageBox.Show("Ayarlar ve tema tercihleri başarıyla kaydedildi.", "Ayarlar Kaydedildi", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }

    private void SelectAutoSyncDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Gün Sonu Paketlerinin Otomatik Kaydedileceği Klasörü Seçin"
            };
            if (dialog.ShowDialog() == true)
            {
                TxtAutoSyncTargetDir.Text = dialog.FolderName;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Klasör seçimi hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var url = TxtUpdateUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            url = UpdateService.DefaultGitHubRepo;
            TxtUpdateUrl.Text = url;
        }

        try
        {
            var update = await UpdateService.CheckForUpdatesAsync(url);
            if (update != null)
            {
                var win = new UpdateWindow(update) { Owner = this };
                win.ShowDialog();
            }
            else
            {
                MessageBox.Show($"Tebrikler! Sisteminiz günceldir.\n\nMevcut Sürüm: v{UpdateService.CurrentVersion}", "Sistem Güncel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Güncelleme kontrolü başarısız:\n{ex.Message}", "Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportBranchData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Mağaza Senkronizasyon Paketini Kaydet",
                Filter = "Pure Accessory Senkronizasyon Paketi (*.sync)|*.sync",
                FileName = $"PureAccessory_Magaza_{DateTime.Now:yyyyMMdd_HHmm}.sync"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var syncService = new BranchSyncService();
                var package = await syncService.CreateSyncPackageAsync(DateTime.Today.AddDays(-30), DateTime.Today, TxtStoreName.Text.Trim());
                await syncService.ExportPackageToFileAsync(package, saveDialog.FileName);

                MessageBox.Show(
                    $"Mağaza verileri başarıyla paketlendi ve kaydedildi!\n\n" +
                    $"📁 Dosya: {saveDialog.FileName}\n" +
                    $"💰 Toplam Ciro: {package.FormattedTotalRevenue}\n" +
                    $"🧾 Satış Adedi: {package.SalesCount}\n\n" +
                    $"Bu dosyayı flash bellek ile veya internet üzerinden merkeze iletebilirsiniz.",
                    "Veri Paketi Hazır", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Paketleme sırasında hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ApplyCorrectionPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Merkezden Gelen Düzeltme Paketini Seçin",
                Filter = "Düzeltme Paketi (*.sync;*.json)|*.sync;*.json|Tüm Dosyalar (*.*)|*.*"
            };

            if (openDialog.ShowDialog() == true)
            {
                var syncService = new BranchSyncService();
                var package = await syncService.LoadPackageFromFileAsync(openDialog.FileName);

                if (!package.IsCorrectionPackage || package.Corrections.Count == 0)
                {
                    MessageBox.Show("Seçilen paket bir düzeltme paketi değil veya düzeltme kaydı içermiyor.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int applied = await syncService.ApplyCorrectionsAsync(package.Corrections);
                MessageBox.Show(
                    $"Merkezden gelen düzeltmeler başarıyla mağaza veritabanına uygulandı!\n\n" +
                    $"• Güncellenen / Düzeltilen Kayıt Sayısı: {applied}",
                    "Düzeltmeler Uygulandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Düzeltme paketi uygulanırken hata:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportFullDatabase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Tüm Veritabanı Yedek / Senkronizasyon Paketini Kaydet",
                Filter = "Pure Accessory Tam Veri Paketi (*.sync)|*.sync|JSON Paketi (*.json)|*.json",
                FileName = $"PureAccessory_TumVeritabani_{DateTime.Now:yyyyMMdd_HHmm}.sync"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var syncService = new BranchSyncService();
                // 2020 yılından bugüne kadar olan TÜM verileri kapsayacak şekilde dışa aktar
                var package = await syncService.CreateSyncPackageAsync(new DateTime(2020, 1, 1), DateTime.Today.AddDays(1), TxtStoreName.Text.Trim());
                await syncService.ExportPackageToFileAsync(package, saveDialog.FileName);

                MessageBox.Show(
                    $"Tüm veritabanı başarıyla paketlendi ve kaydedildi!\n\n" +
                    $"📁 Dosya: {saveDialog.FileName}\n" +
                    $"💰 Toplam Ciro: {package.FormattedTotalRevenue}\n" +
                    $"🧾 Toplam Fiş: {package.SalesCount}\n" +
                    $"💵 Kasa Hareketi: {package.CashTransactions.Count}\n" +
                    $"📆 Kapanış Kaydı: {package.Closures.Count}\n\n" +
                    $"Bu dosyayı USB bellek ile veya ağ üzerinden diğer bilgisayara götürüp 'Veri Paketini İçeri Aktar' butonuyla yükleyebilirsiniz.",
                    "Tam Veri Paketi Hazır", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Tüm veritabanı paketlenirken hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportFullDatabase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "İçeri Aktarılacak Veri Paketini Seçin (.sync veya .json)",
                Filter = "Pure Accessory Veri Paketi (*.sync;*.json)|*.sync;*.json|Tüm Dosyalar (*.*)|*.*"
            };

            if (openDialog.ShowDialog() == true)
            {
                var syncService = new BranchSyncService();
                var package = await syncService.LoadPackageFromFileAsync(openDialog.FileName);

                var ask = MessageBox.Show(
                    $"Seçilen veri paketindeki kayıtlar bu bilgisayara aktarılacaktır:\n\n" +
                    $"• Satış Sayısı: {package.Sales.Count}\n" +
                    $"• Toplam Ciro: {package.FormattedTotalRevenue}\n" +
                    $"• Kasa Hareketleri: {package.CashTransactions.Count}\n" +
                    $"• Kapanış Sayısı: {package.Closures.Count}\n\n" +
                    $"Var olan kayıtlar güncellenecek, eksik kayıtlar eklenecektir.\n\nİşlemi onaylıyor musunuz?",
                    "Veritabanına Aktar Onayı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (ask == MessageBoxResult.Yes)
                {
                    // Tabloların eksiksiz var olduğundan emin ol
                    try
                    {
                        var dbSync = new DatabaseSyncService();
                        await dbSync.EnsureTablesCreatedAsync();
                    }
                    catch { }

                    var result = await syncService.ApplyPackageToDatabaseAsync(package);
                    if (result.Success)
                    {
                        MessageBox.Show(
                            $"Veritabanı aktarımı BAŞARIYLA tamamlandı!\n\n" +
                            $"• Eklenen Yeni Satış: {result.SalesImported}\n" +
                            $"• Güncellenen Satış: {result.SalesUpdated}\n" +
                            $"• Aktarılan Kasa Hareketi: {result.TransactionsImported}\n" +
                            $"• Aktarılan Gün Sonu Kapanışı: {result.ClosuresImported}",
                            "Aktarım Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"Aktarım sırasında hata:\n{result.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Veri paketi yüklenirken hata:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSyncReview_Click(object sender, RoutedEventArgs e)
    {
        var syncWin = new BranchSyncWindow { Owner = this };
        syncWin.ShowDialog();
    }

    private void InstallLocalZipUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Manuel Güncelleme Paketini Seçin (.zip)",
                Filter = "Güncelleme Paketi (*.zip)|*.zip|Tüm Dosyalar (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                var ask = MessageBox.Show(
                    $"Seçilen güncelleme paketi yüklenecektir:\n\n{dlg.FileName}\n\nUygulama otomatik olarak kapanıp güncellenecek ve yeniden başlatılacaktır.\n\nOnaylıyor musunuz?",
                    "Güncellemeyi Uygula",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (ask == MessageBoxResult.Yes)
                {
                    UpdateService.ApplyLocalZipUpdate(dlg.FileName);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Güncelleme başlatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestDbConn_Click(object sender, RoutedEventArgs e)
    {
        var connStr = TxtCustomDbConn.Text?.Trim();
        var dbService = new DatabaseService(string.IsNullOrWhiteSpace(connStr) ? null : connStr);

        var ok = await dbService.TestConnectionAsync();
        if (ok)
        {
            MessageBox.Show($"SQL Server bağlantısı BAŞARILI!\n\nBağlanılan Adres:\n{dbService.ConnectionString}", "Bağlantı Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("SQL Server bağlantısı KURULAMADI!\n\nLütfen SQL Server servisinin çalıştığından ve bağlantı dizesinin doğruluğundan emin olun.", "Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void AutoDetectDb_Click(object sender, RoutedEventArgs e)
    {
        var dbService = new DatabaseService();
        var ok = await dbService.EnsureDatabaseExistsAndDetectAsync();
        if (ok)
        {
            TxtCustomDbConn.Text = dbService.ConnectionString;
            MessageBox.Show($"Yerel SQL Server başarıyla tespit edildi ve ERPSystem veritabanı bağlandı!\n\n{dbService.ConnectionString}", "Algılama Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Yerel bilgisayarda çalışan SQL Server örneği bulunamadı.\nLütfen SQL Server Express servisinin çalıştığını kontrol ediniz.", "Sunucu Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // İptal edildiyse orijinal temaya geri dön
        ThemeService.ApplyTheme(ThemeService.Current);
        DialogResult = false;
        Close();
    }
}
