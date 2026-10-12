using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using ERP.Desktop.Services;
using ERP.Desktop.Views;

namespace ERP.Desktop;

public partial class MainWindow : Window
{
    private readonly Dictionary<Type, Page> _pageCache = new();

    public MainWindow()
    {
        InitializeComponent();
        EnsureLogoLoaded();
        UpdateStoreBranding();
        ThemeService.ThemeChanged += _ => UpdateStoreBranding();
        Closing += MainWindow_Closing;
        ApplyUserPermissions();

        // Telefon Scanner sunucusunu ve HTTPS tünelini arka planda otomatik başlat
        _ = Task.Run(async () =>
        {
            try
            {
                await PhoneScannerService.Instance.StartServerAsync();
            }
            catch { }
        });

        if (AuthService.CurrentUser?.CanAccessDashboard == true)
        {
            NavigateToPage<DashboardView>();
        }
        else if (AuthService.CurrentUser?.CanAccessSales == true)
        {
            NavigateToPage<SalesView>();
        }
        else if (AuthService.CurrentUser?.CanAccessProducts == true)
        {
            NavigateToPage<ProductsView>();
        }
        else
        {
            NavigateToPage<ReportsView>();
        }
    }

    private void EnsureLogoLoaded()
    {
        try
        {
            var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.png");
            if (File.Exists(localPath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(localPath, UriKind.Absolute);
                bmp.EndInit();
                ImgLogo.Source = bmp;
                return;
            }
        }
        catch { }

        try
        {
            var uri = new Uri("pack://application:,,,/ERP.Desktop;component/Assets/logo.png", UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = uri;
            bmp.EndInit();
            ImgLogo.Source = bmp;
        }
        catch { }
    }

    private void MainFrame_Navigating(object? sender, NavigatingCancelEventArgs e)
    {
        if (MainFrame.Content is SalesView salesView && salesView.HasActiveCart)
        {
            if (e.Content is SalesView)
            {
                e.Cancel = true;
                return;
            }

            var result = MessageBox.Show(
                "Satış ekranında açık bir fiş ve eklenmiş ürünler bulunmaktadır!\n\n" +
                "Satış menüsünden çıkabilmek için önce fişi iptal etmelisiniz.\n\n" +
                "Açık fişi şimdi iptal edip menüden çıkmak istiyor musunuz?",
                "Fiş İptali Gerekli",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                salesView.CancelReceipt(promptUser: false);
            }
            else
            {
                e.Cancel = true;
            }
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (MainFrame.Content is SalesView salesView && salesView.HasActiveCart)
        {
            var result = MessageBox.Show(
                "Satış ekranında açık bir fiş ve eklenmiş ürünler bulunmaktadır!\n\n" +
                "Programdan çıkabilmek için önce fişi iptal etmelisiniz.\n\n" +
                "Açık fişi şimdi iptal edip kapatmak istiyor musunuz?",
                "Fiş İptali Gerekli",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                salesView.CancelReceipt(promptUser: false);
            }
            else
            {
                e.Cancel = true;
            }
        }
    }

    private void NavigateToPage<T>() where T : Page, new()
    {
        if (!_pageCache.TryGetValue(typeof(T), out var page))
        {
            page = new T();
            _pageCache[typeof(T)] = page;
        }

        NavigateSafely(page);
    }

    private void NavigateSafely(Page targetPage)
    {
        var user = AuthService.CurrentUser;
        if (user != null && !user.IsAdmin)
        {
            bool hasAccess = targetPage switch
            {
                SalesView => user.CanAccessSales,
                ProductsView => user.CanAccessProducts,
                WarehouseView => user.CanAccessWarehouse,
                CariView => user.CanAccessCari,
                CashView => user.CanAccessCash,
                BankView => user.CanAccessBank,
                InvoicesView => user.CanAccessInvoices,
                ReportsView => user.CanAccessReports,
                DashboardView => user.CanAccessDashboard,
                UsersView => user.CanAccessUsers,
                _ => true
            };

            if (!hasAccess)
            {
                MessageBox.Show(
                    "Bu bölüme erişim yetkiniz kısıtlanmıştır!\n\nLütfen sistem yöneticiniz ile iletişime geçiniz.",
                    "Yetkisiz Erişim",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        if (MainFrame.Content is SalesView salesView && salesView.HasActiveCart)
        {
            if (targetPage is SalesView)
            {
                return;
            }

            var result = MessageBox.Show(
                "Satış ekranında açık bir fiş ve eklenmiş ürünler bulunmaktadır!\n\n" +
                "Satış menüsünden çıkabilmek için önce fişi iptal etmelisiniz.\n\n" +
                "Açık fişi şimdi iptal edip çıkmak istiyor musunuz?",
                "Fiş İptali Gerekli",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                salesView.CancelReceipt(promptUser: false);
                MainFrame.Navigate(targetPage);
            }
            return;
        }

        if (MainFrame.Content == targetPage)
        {
            return;
        }

        try
        {
            MainFrame.Navigate(targetPage);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Sayfa açılırken bir hata oluştu:\n{ex.Message}", "Sayfa Yükleme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyUserPermissions()
    {
        var user = AuthService.CurrentUser;
        if (user == null) return;

        // Modül İzinlerine Göre Menü Görünürlüğü
        BtnNavSales.Visibility = user.CanAccessSales ? Visibility.Visible : Visibility.Collapsed;
        BtnNavProducts.Visibility = user.CanAccessProducts ? Visibility.Visible : Visibility.Collapsed;
        BtnNavWarehouse.Visibility = user.CanAccessWarehouse ? Visibility.Visible : Visibility.Collapsed;
        BtnNavCari.Visibility = user.CanAccessCari ? Visibility.Visible : Visibility.Collapsed;
        BtnNavCash.Visibility = user.CanAccessCash ? Visibility.Visible : Visibility.Collapsed;
        BtnNavBank.Visibility = user.CanAccessBank ? Visibility.Visible : Visibility.Collapsed;
        BtnNavDebts.Visibility = (user.CanAccessDebts || user.IsAdmin) ? Visibility.Visible : Visibility.Collapsed;
        BtnNavInvoices.Visibility = user.CanAccessInvoices ? Visibility.Visible : Visibility.Collapsed;
        BtnNavReports.Visibility = user.CanAccessReports ? Visibility.Visible : Visibility.Collapsed;
        BtnNavDashboard.Visibility = user.CanAccessDashboard ? Visibility.Visible : Visibility.Collapsed;
        BtnNavUsers.Visibility = user.CanAccessUsers ? Visibility.Visible : Visibility.Collapsed;

        // Kullanıcı Rozeti & Adı
        TxtUserName.Text = user.FullName;
        TxtUserRole.Text = user.RoleDisplayName;

        if (user.IsAdmin)
        {
            TxtUserIcon.Text = "👑";
            BadgeUserRole.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"));
            TxtUserRole.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#93C5FD"));
        }
        else
        {
            TxtUserIcon.Text = "👤";
            BadgeUserRole.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
            TxtUserRole.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
        }
    }

    private void SwitchUser_Click(object sender, RoutedEventArgs e)
    {
        if (MainFrame.Content is SalesView salesView && salesView.HasActiveCart)
        {
            var result = MessageBox.Show(
                "Satış ekranında açık bir fiş ve eklenmiş ürünler bulunmaktadır!\n\n" +
                "Kullanıcı değiştirebilmek için önce açık fişi iptal etmelisiniz.\n\n" +
                "Açık fişi şimdi iptal edip kullanıcı değiştirmek istiyor musunuz?",
                "Fiş İptali Gerekli",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                salesView.CancelReceipt(promptUser: false);
            }
            else
            {
                return;
            }
        }

        // ERP'yi kapatıp login ekranına dön
        AuthService.Logout();
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        this.Close();

        var loginWin = new LoginWindow();
        if (loginWin.ShowDialog() == true)
        {
            var newMainWindow = new MainWindow();
            Application.Current.MainWindow = newMainWindow;
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            newMainWindow.Show();
        }
        else
        {
            try
            {
                Application.Current.Shutdown();
            }
            catch
            {
                Environment.Exit(0);
            }
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var win = new SettingsWindow { Owner = this };
            if (win.ShowDialog() == true)
            {
                UpdateStoreBranding();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ayarlar açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateStoreBranding()
    {
        try
        {
            var settings = ThemeService.Current;
            var curVer = UpdateService.CurrentVersion;
            if (TxtBrandTitle != null) TxtBrandTitle.Text = settings.StoreName;
            if (TxtBrandSubtitle != null) TxtBrandSubtitle.Text = settings.StoreSubtitle;
            if (TxtSidebarVersion != null) TxtSidebarVersion.Text = $"v{curVer}";
            Title = $"{settings.StoreName} — {settings.StoreSubtitle} (v{curVer})";

            // Kompakt Mod (Sidebar Genişliği)
            if (SidebarColumn != null)
            {
                SidebarColumn.Width = settings.CompactMode ? new GridLength(185) : new GridLength(230);
            }

            // Yazı Tipi (Font)
            if (!string.IsNullOrWhiteSpace(settings.AppFontFamily))
            {
                FontFamily = new FontFamily(settings.AppFontFamily);
            }
        }
        catch { }
    }

    private void Dashboard_Click(object s, RoutedEventArgs e) => NavigateToPage<DashboardView>();
    private void Sales_Click(object s, RoutedEventArgs e) => NavigateToPage<SalesView>();
    private void Products_Click(object s, RoutedEventArgs e) => NavigateToPage<ProductsView>();
    private void Warehouse_Click(object s, RoutedEventArgs e) => NavigateToPage<WarehouseView>();
    private void Cari_Click(object s, RoutedEventArgs e) => NavigateToPage<CariView>();
    private void Cash_Click(object s, RoutedEventArgs e) => NavigateToPage<CashView>();
    private void Bank_Click(object s, RoutedEventArgs e) => NavigateToPage<BankView>();
    private void Debts_Click(object s, RoutedEventArgs e)
    {
        try
        {
            NavigateToPage<DebtsView>();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Borç & Finans sayfası yüklenirken hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void Invoices_Click(object s, RoutedEventArgs e) => NavigateToPage<InvoicesView>();
    private void Reports_Click(object s, RoutedEventArgs e) => NavigateToPage<ReportsView>();
    private void Users_Click(object s, RoutedEventArgs e)
    {
        try
        {
            NavigateToPage<UsersView>();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kullanıcı Yönetimi sayfası yüklenirken hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
