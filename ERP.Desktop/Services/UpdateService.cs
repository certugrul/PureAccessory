using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using ERP.Desktop.Models;

namespace ERP.Desktop.Services;

public class UpdateInfo
{
    public string Version { get; set; } = "1.0.0";
    public string ReleaseDate { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ChangeLog { get; set; } = "";
    public bool Mandatory { get; set; } = false;
}

public static class UpdateService
{
    /// <summary>
    /// ERP sisteminin gömülü (varsayılan) GitHub depo adresi.
    /// Kullanıcı ayar yapmasa dahi doğrudan buradan güncellemeleri kontrol eder.
    /// </summary>
    public const string DefaultGitHubRepo = "https://github.com/certugrul/PureAccessory";

    /// <summary>
    /// Geliştirme ortamı (Visual Studio kaynak kod klasörü) tespiti.
    /// Kaynak makinede çalışırken internetten kendi yazdığı sürümü tekrar indirmeye çalışmasını engeller.
    /// </summary>
    public static bool IsDevelopmentEnvironment
    {
        get
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                // Eğer bin\Debug veya bin\Release altındaysa veya kaynak repo kökünde .sln varsa
                if (baseDir.Contains(@"\bin\Debug") || baseDir.Contains(@"\bin\Release"))
                {
                    return true;
                }

                var parent = Directory.GetParent(baseDir);
                while (parent != null)
                {
                    if (File.Exists(Path.Combine(parent.FullName, "ERP.sln")))
                    {
                        return true;
                    }
                    parent = parent.Parent;
                }
            }
            catch { }
            return false;
        }
    }

    private static readonly HttpClient _httpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        // GitHub API için zorunlu User-Agent başlığı
        client.DefaultRequestHeaders.Add("User-Agent", "PureAccessory-ERP-Updater/1.0");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json, application/json, text/plain, */*");
        return client;
    }

    public static string CurrentVersion
    {
        get
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(infoVer))
                {
                    // Eğer git commit sha eki varsa (örn: 1.0.7+abc1234) temizle
                    var clean = infoVer.Split('+')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(clean)) return clean;
                }

                var fileVer = FileVersionInfo.GetVersionInfo(asm.Location).FileVersion;
                if (!string.IsNullOrWhiteSpace(fileVer) && fileVer != "1.0.0.0")
                {
                    var parts = fileVer.Split('.');
                    return parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : fileVer;
                }

                var ver = asm.GetName().Version;
                return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }
    }

    /// <summary>
    /// GitHub Releases API'sini veya doğrudan belirtilen JSON URL'sini kullanarak
    /// en son sürümü ve .zip indirme linkini dinamik olarak çeker.
    /// URL boş bırakılırsa gömülü DefaultGitHubRepo adresini kullanır.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdatesAsync(string? explicitUrl = null)
    {
        var target = explicitUrl;
        if (string.IsNullOrWhiteSpace(target))
        {
            target = ThemeService.Current?.UpdateCheckUrl;
        }

        // Eğer ayarlarda da boşsa, gömülü varsayılan GitHub reponu kullan
        if (string.IsNullOrWhiteSpace(target))
        {
            target = DefaultGitHubRepo;
        }

        target = target.Trim();

        try
        {
            // 1. GitHub Releases Kontrolü
            if (IsGitHubTarget(target))
            {
                var ghUpdate = await CheckGitHubReleasesAsync(target);
                if (ghUpdate != null && IsNewerVersion(ghUpdate.Version, CurrentVersion))
                {
                    return ghUpdate;
                }
                return null;
            }

            // 2. Standart version.json veya web sunucusu kontrolü
            var response = await _httpClient.GetStringAsync(target);
            var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateInfo == null || string.IsNullOrWhiteSpace(updateInfo.Version))
            {
                return null;
            }

            if (IsNewerVersion(updateInfo.Version, CurrentVersion))
            {
                return updateInfo;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] Güncelleme kontrol hatası: {ex.Message}");
        }

        return null;
    }

    private static bool IsGitHubTarget(string target)
    {
        return target.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
               target.Contains("api.github.com", StringComparison.OrdinalIgnoreCase) ||
               (target.Count(c => c == '/') == 1 && !target.Contains(":") && !target.Contains(".")); // "owner/repo" biçimi
    }

    /// <summary>
    /// GitHub Releases API (api.github.com/repos/{owner}/{repo}/releases/latest) üzerinden
    /// en son sürüm numarasını, yayın tarihini, sürüm notlarını ve .zip asset indirme linkini çeker.
    /// </summary>
    private static async Task<UpdateInfo?> CheckGitHubReleasesAsync(string target)
    {
        string apiUrl;

        if (target.StartsWith("https://api.github.com/repos/", StringComparison.OrdinalIgnoreCase))
        {
            apiUrl = target;
        }
        else if (target.Contains("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            // Örnek: https://github.com/owner/repo veya https://github.com/owner/repo/releases
            var trimmed = target.Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("http://github.com/", "", StringComparison.OrdinalIgnoreCase)
                                .Trim('/');
            var segments = trimmed.Split('/');
            if (segments.Length >= 2)
            {
                apiUrl = $"https://api.github.com/repos/{segments[0]}/{segments[1]}/releases/latest";
            }
            else
            {
                return null;
            }
        }
        else if (target.Count(c => c == '/') == 1)
        {
            // Örnek format: "owner/repo"
            apiUrl = $"https://api.github.com/repos/{target}/releases/latest";
        }
        else
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[UpdateService] GitHub API yanıt kodu: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Tag name / sürüm belirle (örn: "v1.0.8" veya "1.0.8")
            string tagName = "";
            if (root.TryGetProperty("tag_name", out var tagElem))
            {
                tagName = tagElem.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(tagName) && root.TryGetProperty("name", out var nameElem))
            {
                tagName = nameElem.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            var cleanVersion = tagName.TrimStart('v', 'V').Trim();

            // Sürüm Tarihi
            string releaseDate = "";
            if (root.TryGetProperty("published_at", out var pubElem))
            {
                if (DateTime.TryParse(pubElem.GetString(), out var dt))
                {
                    releaseDate = dt.ToString("dd.MM.yyyy HH:mm");
                }
            }

            // Sürüm Değişiklik Notları (Changelog / Body)
            string body = "";
            if (root.TryGetProperty("body", out var bodyElem))
            {
                body = bodyElem.GetString() ?? "";
            }

            // Releases'a yüklenen ZIP dosyasını (Asset) bul
            string downloadUrl = "";
            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                // Önce .zip uzantılı asset ara
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        if (asset.TryGetProperty("browser_download_url", out var dlUrl))
                        {
                            downloadUrl = dlUrl.GetString() ?? "";
                            break;
                        }
                    }
                }
            }

            // Eğer release asset'lerinde zip bulunamadıysa GitHub'ın kaynak zip'ini (zipball_url) fallback olarak kullan
            if (string.IsNullOrWhiteSpace(downloadUrl) && root.TryGetProperty("zipball_url", out var zipballElem))
            {
                downloadUrl = zipballElem.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return null;
            }

            return new UpdateInfo
            {
                Version = cleanVersion,
                ReleaseDate = releaseDate,
                DownloadUrl = downloadUrl,
                ChangeLog = !string.IsNullOrWhiteSpace(body) ? body : $"GitHub Release: {tagName}",
                Mandatory = false
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] GitHub Release ayrıştırma hatası: {ex.Message}");
            return null;
        }
    }

    public static bool IsNewerVersion(string remoteVersionStr, string localVersionStr)
    {
        try
        {
            var remote = ParseVersion(remoteVersionStr);
            var local = ParseVersion(localVersionStr);
            return remote > local;
        }
        catch
        {
            return false;
        }
    }

    private static Version ParseVersion(string verStr)
    {
        var parts = verStr.TrimStart('v', 'V').Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out var ma) ? ma : 1;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out var mi) ? mi : 0;
        int build = parts.Length > 2 && int.TryParse(parts[2], out var bu) ? bu : 0;
        return new Version(major, minor, build);
    }

    public static async Task DownloadAndApplyUpdateAsync(UpdateInfo update, IProgress<int>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            throw new InvalidOperationException("Güncelleme indirme linki geçersiz.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "PureAccessoryUpdate");
        if (!Directory.Exists(tempDir))
        {
            Directory.CreateDirectory(tempDir);
        }

        var tempZip = Path.Combine(tempDir, $"update_v{update.Version}.zip");

        // 1. Zip dosyasını indir
        using (var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                totalRead += read;

                if (totalBytes > 0 && progress != null)
                {
                    int pct = (int)((totalRead * 100) / totalBytes);
                    progress.Report(pct);
                }
            }
        }

        // 2. Updater.exe'yi bul
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var updaterPath = Path.Combine(appDir, "Tools", "Updater.exe");
        if (!File.Exists(updaterPath))
        {
            updaterPath = Path.Combine(appDir, "Updater.exe");
        }

        if (!File.Exists(updaterPath))
        {
            // Eğer Updater.exe henüz Tools içinde yoksa üst veya bin klasöründe ara
            var altPath = Path.Combine(appDir, "..", "..", "..", "..", "ERP.Updater", "bin", "Release", "net10.0-windows", "Updater.exe");
            if (File.Exists(altPath))
            {
                updaterPath = altPath;
            }
        }

        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException("Updater.exe güncelleyici bileşeni bulunamadı.");
        }

        // 3. Updater.exe'yi çalıştır ve ERP'yi kapat
        var currentPid = Process.GetCurrentProcess().Id;
        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = $"--pid {currentPid} --appDir \"{appDir.TrimEnd('\\')}\" --zip \"{tempZip}\" --exe \"ERP.Desktop.exe\"",
            UseShellExecute = true,
            WorkingDirectory = appDir
        };

        Process.Start(startInfo);

        // 4. ERP'yi kapat
        Application.Current.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
        Environment.Exit(0);
    }

    public static void ApplyLocalZipUpdate(string zipFilePath)
    {
        if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
        {
            throw new FileNotFoundException("Güncelleme zip paketi bulunamadı.", zipFilePath);
        }

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var updaterPath = Path.Combine(appDir, "Tools", "Updater.exe");
        if (!File.Exists(updaterPath))
        {
            updaterPath = Path.Combine(appDir, "Updater.exe");
        }

        if (!File.Exists(updaterPath))
        {
            var altPath = Path.Combine(appDir, "..", "..", "..", "..", "ERP.Updater", "bin", "Release", "net10.0-windows", "Updater.exe");
            if (File.Exists(altPath))
            {
                updaterPath = altPath;
            }
        }

        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException("Updater.exe güncelleyici bileşeni bulunamadı.");
        }

        var currentPid = Process.GetCurrentProcess().Id;
        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = $"--pid {currentPid} --appDir \"{appDir.TrimEnd('\\')}\" --zip \"{zipFilePath}\" --exe \"ERP.Desktop.exe\"",
            UseShellExecute = true,
            WorkingDirectory = appDir
        };

        Process.Start(startInfo);

        Application.Current.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
        Environment.Exit(0);
    }
}
