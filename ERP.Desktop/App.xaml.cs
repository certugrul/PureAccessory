using System.IO;
using System.Windows;

namespace ERP.Desktop;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            try { File.WriteAllText("global_crash.txt", "DISPATCHER ERROR:\n" + e.Exception.ToString()); } catch { }
            MessageBox.Show(
                $"İşlem sırasında bir hata oluştu:\n\n{e.Exception.Message}",
                "Hata Bildirimi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                try { File.WriteAllText("global_crash.txt", "APPDOMAIN ERROR:\n" + ex.ToString()); } catch { }
                MessageBox.Show(
                    $"Kritik sistem hatası:\n\n{ex.Message}",
                    "Kritik Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window w && w.Icon == null)
            {
                try
                {
                    w.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                        new Uri("pack://application:,,,/ERP.Desktop;component/Assets/AppLogo.ico", UriKind.Absolute));
                }
                catch { }
            }
        }));

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            // 1. Tema ve ayarları anında yükle (~2 ms)
            try
            {
                Services.ThemeService.Load();
            }
            catch { }

            // 2. Arka planda: .old_bak temizliği, veritabanı ısındırma ve güncelleme kontrolü (UI'ı hiç bekletmeden!)
            bool shouldCheckUpdates = !e.Args.Contains("--no-update-check") && Services.ThemeService.Current?.AutoCheckUpdates != false;
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var bakFile in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.old_bak", SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(bakFile); } catch { }
                    }
                }
                catch { }

                if (shouldCheckUpdates)
                {
                    try
                    {
                        var update = await Services.UpdateService.CheckForUpdatesAsync().ConfigureAwait(false);
                        if (update != null)
                        {
                            await Current.Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    var updateWin = new Views.UpdateWindow(update);
                                    if (Current.Windows.Count > 0)
                                    {
                                        foreach (Window win in Current.Windows)
                                        {
                                            if (win.IsVisible) { updateWin.Owner = win; break; }
                                        }
                                    }
                                    bool? dialogResult = updateWin.ShowDialog();
                                    if (update.Mandatory && dialogResult != true)
                                    {
                                        Shutdown(0);
                                    }
                                }
                                catch { }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[App] Arka plan güncelleme kontrolü: {ex.Message}");
                    }
                }
            });

            // 3. Login ekranını ANINDA (0 saniye bekleme ile) aç
            var loginWin = new Views.LoginWindow();
            bool? result = loginWin.ShowDialog();

            if (result == true)
            {
                var mainWin = new MainWindow();
                MainWindow = mainWin;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWin.Show();

                // Bekleyen veya gönderilememiş Z Raporu e-postalarını arka planda yeniden dene
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var emailService = new Services.ZReportEmailService();
                        await emailService.ProcessPendingQueueAsync();
                    }
                    catch { }
                });
            }
            else
            {
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uygulama başlatılırken hata oluştu:\n\n{ex.Message}\n\n{ex.StackTrace}", 
                            "Başlatma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
