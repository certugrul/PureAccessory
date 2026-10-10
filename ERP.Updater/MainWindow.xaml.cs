using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;

namespace ERP.Updater;

public partial class MainWindow : Window
{
    private readonly string[] _args;

    public MainWindow(string[] args)
    {
        InitializeComponent();
        _args = args;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var infoVer = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var verStr = !string.IsNullOrWhiteSpace(infoVer) ? infoVer.Split('+')[0].Trim() : "1.0.7";
            if (TxtUpdaterVersion != null)
            {
                TxtUpdaterVersion.Text = $"v{verStr}";
            }
        }
        catch { }

        await Task.Delay(200); // UI çizilsin
        await RunUpdateProcessAsync();
    }

    private async Task RunUpdateProcessAsync()
    {
        try
        {
            int pid = 0;
            string appDir = "";
            string zipPath = "";
            string exeName = "ERP.Desktop.exe";

            for (int i = 0; i < _args.Length; i++)
            {
                if (_args[i] == "--pid" && i + 1 < _args.Length)
                {
                    int.TryParse(_args[i + 1], out pid);
                }
                else if (_args[i] == "--appDir" && i + 1 < _args.Length)
                {
                    appDir = _args[i + 1];
                }
                else if (_args[i] == "--zip" && i + 1 < _args.Length)
                {
                    zipPath = _args[i + 1];
                }
                else if (_args[i] == "--exe" && i + 1 < _args.Length)
                {
                    exeName = _args[i + 1];
                }
            }

            if (string.IsNullOrWhiteSpace(appDir))
            {
                // Varsayılan olarak bir üst klasör veya çalışma klasörü
                appDir = AppDomain.CurrentDomain.BaseDirectory;
                if (appDir.EndsWith("Tools\\") || appDir.EndsWith("Tools/"))
                {
                    appDir = Directory.GetParent(appDir.TrimEnd('\\', '/'))?.FullName ?? appDir;
                }
            }

            // 1. Ana uygulamanın tamamen kapanmasını bekle
            if (pid > 0)
            {
                UpdateStatus("Uygulamanın kapanması bekleniyor...");
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        await Task.Run(() => proc.WaitForExit(8000));
                        if (!proc.HasExited)
                        {
                            proc.Kill();
                            await Task.Delay(1000);
                        }
                    }
                }
                catch { }
            }

            // Ayrıca çalışan başka ERP.Desktop processleri varsa güvenle bekle
            await Task.Delay(1000);

            // 2. Zip dosyasını aç ve güncelle
            if (!string.IsNullOrWhiteSpace(zipPath) && File.Exists(zipPath))
            {
                UpdateStatus("Yeni sürüm dosyaları yükleniyor...");
                await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    int total = archive.Entries.Count;
                    int current = 0;

                    foreach (var entry in archive.Entries)
                    {
                        current++;
                        if (string.IsNullOrEmpty(entry.Name)) continue; // Klasör girdisi

                        // Kullanıcının özel ayarlarını ve veritabanı dosyalarını ASLA ezme
                        var relativePath = entry.FullName.Replace('/', '\\');
                        if (relativePath.StartsWith("Data\\settings.json", StringComparison.OrdinalIgnoreCase) ||
                            relativePath.StartsWith("Data\\users.json", StringComparison.OrdinalIgnoreCase) ||
                            relativePath.EndsWith(".mdf", StringComparison.OrdinalIgnoreCase) ||
                            relativePath.EndsWith(".ldf", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetPathCheck = Path.Combine(appDir, relativePath);
                            if (File.Exists(targetPathCheck))
                            {
                                continue; // Var olan kullanıcı verisini koru
                            }
                        }

                        var destinationPath = Path.Combine(appDir, relativePath);
                        var destinationDir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                        {
                            Directory.CreateDirectory(destinationDir);
                        }

                        // Dosyayı üzerine yaz
                        try
                        {
                            entry.ExtractToFile(destinationPath, overwrite: true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Dosya açılamadı ({entry.FullName}): {ex.Message}");
                        }
                    }
                });

                // Geçici zip dosyasını temizle
                try
                {
                    File.Delete(zipPath);
                }
                catch { }
            }
            else
            {
                UpdateStatus("Güncelleme paketi bulunamadı!");
                await Task.Delay(2000);
            }

            // 3. ERP'yi yeniden başlat
            UpdateStatus("Pure Accessory ERP başlatılıyor...");
            await Task.Delay(1000);

            var mainExePath = Path.Combine(appDir, exeName);
            if (File.Exists(mainExePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = mainExePath,
                    WorkingDirectory = appDir,
                    UseShellExecute = true
                });
            }

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Güncelleme uygulanırken hata oluştu:\n{ex.Message}", "Güncelleme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private void UpdateStatus(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = message;
        });
    }
}
