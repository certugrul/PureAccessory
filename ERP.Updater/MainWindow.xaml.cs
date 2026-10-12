using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;

namespace ERP.Updater;

public partial class MainWindow : Window
{
    private readonly string[] _args;
    private string _logPath = "";

    public MainWindow(string[] args)
    {
        InitializeComponent();
        _args = args;
        Loaded += MainWindow_Loaded;
    }

    private void Log(string message)
    {
        try
        {
            Debug.WriteLine($"[Updater] {message}");
            if (!string.IsNullOrEmpty(_logPath))
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
            }
        }
        catch { }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var verStr = !string.IsNullOrWhiteSpace(infoVer) ? infoVer.Split('+')[0].Trim() : "1.0.34";
            if (TxtUpdaterVersion != null)
            {
                TxtUpdaterVersion.Text = $"v{verStr}";
            }
        }
        catch { }

        await Task.Delay(300); // UI çizilsin
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

            // 1. Argümanları titizlikle ayrıştır
            for (int i = 0; i < _args.Length; i++)
            {
                var arg = _args[i]?.Trim();
                if (string.IsNullOrEmpty(arg)) continue;

                if (arg.Equals("--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < _args.Length)
                {
                    int.TryParse(_args[i + 1]?.Trim('"', ' '), out pid);
                }
                else if (arg.Equals("--appDir", StringComparison.OrdinalIgnoreCase) && i + 1 < _args.Length)
                {
                    appDir = _args[i + 1]?.Trim('"', ' ') ?? "";
                }
                else if (arg.Equals("--zip", StringComparison.OrdinalIgnoreCase) && i + 1 < _args.Length)
                {
                    zipPath = _args[i + 1]?.Trim('"', ' ') ?? "";
                }
                else if (arg.Equals("--exe", StringComparison.OrdinalIgnoreCase) && i + 1 < _args.Length)
                {
                    exeName = _args[i + 1]?.Trim('"', ' ') ?? "ERP.Desktop.exe";
                }
            }

            // Eğer appDir boşsa Updater'ın bulunduğu klasörün bir üst klasörünü hedef al
            if (string.IsNullOrWhiteSpace(appDir))
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                if (baseDir.EndsWith("Tools", StringComparison.OrdinalIgnoreCase))
                {
                    appDir = Directory.GetParent(baseDir)?.FullName ?? baseDir;
                }
                else
                {
                    appDir = baseDir;
                }
            }

            _logPath = Path.Combine(appDir, "updater_log.txt");
            Log("==================================================");
            Log($"Güncelleyici başlatıldı. PID: {pid}, AppDir: {appDir}, Zip: {zipPath}");

            // Eğer zipPath boşsa veya dosya yoksa, Temp klasöründeki en son zip'i ara
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                var tempFolder = Path.Combine(Path.GetTempPath(), "PureAccessoryUpdate");
                if (Directory.Exists(tempFolder))
                {
                    var latestZip = Directory.GetFiles(tempFolder, "*.zip")
                                             .OrderByDescending(f => File.GetLastWriteTime(f))
                                             .FirstOrDefault();
                    if (!string.IsNullOrEmpty(latestZip))
                    {
                        zipPath = latestZip;
                        Log($"Zip doğrudan argümandan alınamadı, Temp klasöründen bulundu: {zipPath}");
                    }
                }
            }

            // 2. Ana uygulamanın ve olası tüm ERP.Desktop işlemlerinin kapanmasını bekle ve sonlandır
            UpdateStatus("Eski uygulamanın kapanması bekleniyor...");
            Log("Çalışan ERP.Desktop süreçleri denetleniyor...");

            if (pid > 0)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        Log($"PID {pid} işlemi sonlandırılıyor...");
                        proc.Kill();
                        await Task.Run(() => proc.WaitForExit(4000));
                    }
                }
                catch { }
            }

            // Arka planda kalmış tüm ERP.Desktop süreçlerini zorla kapat
            var activeErpProcesses = Process.GetProcessesByName("ERP.Desktop");
            foreach (var p in activeErpProcesses)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        Log($"Arka plandaki ERP.Desktop (PID: {p.Id}) sonlandırılıyor...");
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
            }

            // İşletim sisteminin dosya handle kilitlerini tamamen serbest bırakması için bekle
            await Task.Delay(1500);

            // 3. Zip dosyasını aç ve dosyaları güncelle
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                Log($"HATA: Güncelleme zip paketi bulunamadı! Aranan yol: {zipPath}");
                UpdateStatus("Hata: Güncelleme paketi bulunamadı!");
                MessageBox.Show($"Güncelleme paketi (.zip) bulunamadı:\n{zipPath}\n\nLütfen tekrar güncellemeyi deneyin.", 
                                "Güncelleme Paketi Eksik", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            UpdateStatus("Yeni sürüm dosyaları sisteme yazılıyor...");
            Log($"Zip paketi açılıyor: {zipPath} -> {appDir}");

            int extractedCount = 0;
            int errorCount = 0;
            var failedFiles = new List<string>();

            await Task.Run(async () =>
            {
                using var archive = ZipFile.OpenRead(zipPath);
                int totalEntries = archive.Entries.Count;
                Log($"Zip içindeki toplam girdi sayısı: {totalEntries}");

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // Klasör girdisi

                    var relativePath = entry.FullName.Replace('/', '\\');

                    // Kendi kendine çalışan Updater.exe'yi ezmeye çalışma (kilitlidir)
                    if (relativePath.EndsWith("Updater.exe", StringComparison.OrdinalIgnoreCase) ||
                        relativePath.EndsWith("Updater.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Kullanıcı veritabanı ve yerel kullanıcı ayarlarını ASLA ezme
                    if (relativePath.StartsWith("Data\\settings.json", StringComparison.OrdinalIgnoreCase) ||
                        relativePath.StartsWith("Data\\users.json", StringComparison.OrdinalIgnoreCase) ||
                        relativePath.EndsWith(".mdf", StringComparison.OrdinalIgnoreCase) ||
                        relativePath.EndsWith(".ldf", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetCheck = Path.Combine(appDir, relativePath);
                        if (File.Exists(targetCheck))
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

                    // Dosyayı üzerine yaz - 5 defa retry mekanizması
                    bool written = false;
                    for (int attempt = 1; attempt <= 5; attempt++)
                    {
                        try
                        {
                            // Eğer hedef dosya varsa salt okunur (ReadOnly) özniteliğini kaldır
                            if (File.Exists(destinationPath))
                            {
                                File.SetAttributes(destinationPath, FileAttributes.Normal);
                            }

                            entry.ExtractToFile(destinationPath, overwrite: true);
                            written = true;
                            extractedCount++;
                            break;
                        }
                        catch (IOException)
                        {
                            if (attempt < 5)
                            {
                                Thread.Sleep(300);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Dosya yazma hatası ({relativePath}) deneme {attempt}: {ex.Message}");
                            if (attempt < 5) Thread.Sleep(300);
                        }
                    }

                    if (!written)
                    {
                        errorCount++;
                        failedFiles.Add(relativePath);
                        Log($"UYARI: Dosya yazılamadı: {relativePath}");
                    }
                }
            });

            Log($"Zip açma işlemi bitti. Başarılı: {extractedCount}, Hatalı: {errorCount}");

            if (errorCount > 0 && failedFiles.Any(f => f.Contains("ERP.Desktop.dll") || f.Contains("ERP.Desktop.exe")))
            {
                string errMsg = $"Bazı kritik sistem dosyaları güncellenemedi ({errorCount} dosya):\n" +
                               string.Join("\n", failedFiles.Take(5)) +
                               "\n\nLütfen ERP'nin açık olmadığından veya başka bir programın dosyaları kilitlemediğinden emin olun.";
                Log($"KRİTİK HATA: {errMsg}");
                MessageBox.Show(errMsg, "Güncelleme Kısmi Başarısız", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                Log("Tüm dosyalar başarıyla hedef klasöre aktarıldı!");
                // Geçici zip dosyasını temizle
                try
                {
                    File.Delete(zipPath);
                    Log("Geçici zip dosyası silindi.");
                }
                catch { }
            }

            // 4. ERP uygulamasını yeni sürümüyle başlat
            UpdateStatus("Pure Accessory ERP başlatılıyor...");
            Log("ERP.Desktop.exe başlatılıyor...");
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
                Log($"Uygulama başarıyla başlatıldı: {mainExePath}");
            }
            else
            {
                Log($"HATA: Ana uygulama bulunamadı: {mainExePath}");
                MessageBox.Show($"ERP başlatılamadı. Dosya bulunamadı:\n{mainExePath}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log($"Genel Güncelleme Hatası: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show($"Güncelleme uygulanırken beklenmeyen bir hata oluştu:\n{ex.Message}\n\nDetaylar: {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updater_log.txt")}", 
                            "Güncelleme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private void UpdateStatus(string message)
    {
        Dispatcher.Invoke(() =>
        {
            if (TxtStatus != null)
            {
                TxtStatus.Text = message;
            }
        });
    }
}
