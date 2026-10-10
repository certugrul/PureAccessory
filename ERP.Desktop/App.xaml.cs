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

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (e.Args.Contains("--import-historical"))
        {
            try
            {
                var importer = new Services.HistoricalDataImportService();
                var result = Task.Run(async () => await importer.ExecuteImportAsync()).Result;
                Console.WriteLine("=================================================");
                Console.WriteLine("Import tamamlandı.");
                Console.WriteLine($"Başlangıç tarihi: {result.StartDate:dd.MM.yyyy}");
                Console.WriteLine($"Bitiş tarihi: {result.EndDate:dd.MM.yyyy}");
                Console.WriteLine($"Aktarılan gün: {result.ImportedDays}");
                Console.WriteLine($"Aktarılan satış: {result.TotalRevenue:N2} TL");
                Console.WriteLine($"Aktarılan gider: {result.TotalExpenses:N2} TL");
                Console.WriteLine($"Aktarılan ciro: {result.TotalRevenue:N2} TL");
                Console.WriteLine($"Aktarılan IBAN kapanışı: {result.TotalIbanClosures:N2} TL");
                Console.WriteLine($"Uyarı sayısı: {result.WarningCount}");
                Console.WriteLine($"Hata sayısı: {result.ErrorCount}");
                Console.WriteLine("=================================================");
                Console.WriteLine($"IBAN KAPANIŞ RAPORU ({result.IbanClosures.Count} Gün):");
                foreach (var iban in result.IbanClosures)
                {
                    Console.WriteLine($"  {iban.Date:dd.MM.yyyy} | Kredi Satış: {iban.KrediSatis:N2} TL | Z Raporu Kredi: {iban.ZRaporuKredi:N2} TL | IBAN: {iban.IbanTutari:N2} TL");
                }
                Console.WriteLine("=================================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IMPORT_FAILED: {ex}");
            }
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--migrate"))
        {
            try
            {
                Task.Run(async () => await Services.DatabaseSyncService.InitializeAndMigrateAsync()).Wait();
                Console.WriteLine("MIGRATION_COMPLETED_SUCCESSFULLY");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MIGRATION_FAILED: {ex.Message}");
            }
            Shutdown(0);
            return;
        }

        try
        {
            // Tema ve ayarları yükle
            try
            {
                Services.ThemeService.Load();
            }
            catch { }

            // SQL Server ERPSystem veritabanı tablolarını ve JSON senkronizasyonunu başlat
            try
            {
                Task.Run(async () => await Services.DatabaseSyncService.InitializeAndMigrateAsync()).Wait();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] DB Init hatası: {ex.Message}");
            }

            var loginWin = new Views.LoginWindow();
            bool? result = loginWin.ShowDialog();

            if (result == true)
            {
                var mainWin = new MainWindow();
                MainWindow = mainWin;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWin.Show();

                // Arka planda uzaktan güncelleme kontrolü yap (Sadece dağıtılan istemcilerde, geliştirici/kaynak PC'de tetiklenmez)
                if (!Services.UpdateService.IsDevelopmentEnvironment && Services.ThemeService.Current?.AutoCheckUpdates != false)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        try
                        {
                            var update = await Services.UpdateService.CheckForUpdatesAsync();
                            if (update != null)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    var updateWin = new Views.UpdateWindow(update) { Owner = mainWin };
                                    updateWin.ShowDialog();
                                });
                            }
                        }
                        catch { }
                    });
                }
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
