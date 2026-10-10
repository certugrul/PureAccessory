using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ERP.Desktop.Models;

namespace ERP.Desktop.Services;

public static class ThemeService
{
    private static readonly string SettingsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");
    private static AppSettings? _currentSettings;

    public static event Action<AppSettings>? ThemeChanged;

    public static AppSettings Current
    {
        get
        {
            if (_currentSettings == null)
            {
                Load();
            }
            return _currentSettings!;
        }
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                _currentSettings = JsonSerializer.Deserialize<AppSettings>(json);
            }
        }
        catch { }

        _currentSettings ??= new AppSettings();
        ApplyTheme(_currentSettings);
    }

    public static void Save(AppSettings settings)
    {
        _currentSettings = settings;
        try
        {
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }

        ApplyTheme(settings);
        ThemeChanged?.Invoke(settings);
    }

    public static void ApplyTheme(AppSettings settings)
    {
        try
        {
            if (Application.Current != null)
            {
                // 1. Ana Vurgu Rengi ve Türevleri
                if (!string.IsNullOrWhiteSpace(settings.PrimaryColor))
                {
                    var color = (Color)ColorConverter.ConvertFromString(settings.PrimaryColor);
                    var primaryBrush = new SolidColorBrush(color);
                    primaryBrush.Freeze();
                    Application.Current.Resources["PrimaryBrush"] = primaryBrush;

                    // Çok açık ton (rozet ve buton arkaplanları için)
                    var lightColor = Color.FromArgb(35, color.R, color.G, color.B);
                    var lightBrush = new SolidColorBrush(lightColor);
                    lightBrush.Freeze();
                    Application.Current.Resources["PrimaryLightBrush"] = lightBrush;

                    // Hover tonu (bir tık koyusu)
                    byte rH = (byte)Math.Max(0, color.R - 25);
                    byte gH = (byte)Math.Max(0, color.G - 25);
                    byte bH = (byte)Math.Max(0, color.B - 25);
                    var hoverBrush = new SolidColorBrush(Color.FromRgb(rH, gH, bH));
                    hoverBrush.Freeze();
                    Application.Current.Resources["PrimaryHoverBrush"] = hoverBrush;
                }

                // 2. Sol Menü Rengi
                if (!string.IsNullOrWhiteSpace(settings.SidebarColor))
                {
                    var color = (Color)ColorConverter.ConvertFromString(settings.SidebarColor);
                    var sidebarBrush = new SolidColorBrush(color);
                    sidebarBrush.Freeze();
                    Application.Current.Resources["SidebarBrush"] = sidebarBrush;
                }

                // 3. Karanlık / Aydınlık Mod ve Sayfa Arka Planları
                if (settings.DarkModeOption == "Koyu")
                {
                    Application.Current.Resources["PageBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    Application.Current.Resources["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                    Application.Current.Resources["CardBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                    Application.Current.Resources["TextPrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                    Application.Current.Resources["TextSecondaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                }
                else if (settings.DarkModeOption == "Gece Siyahı (OLED)")
                {
                    Application.Current.Resources["PageBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000000"));
                    Application.Current.Resources["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121212"));
                    Application.Current.Resources["CardBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27272A"));
                    Application.Current.Resources["TextPrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                    Application.Current.Resources["TextSecondaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A1A1AA"));
                }
                else if (settings.DarkModeOption == "Yumuşak Gri")
                {
                    Application.Current.Resources["PageBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                    Application.Current.Resources["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                    Application.Current.Resources["CardBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                    Application.Current.Resources["TextPrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    Application.Current.Resources["TextSecondaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                }
                else
                {
                    // Varsayılan Açık (Modern Temiz Beyaz/Gri)
                    Application.Current.Resources["PageBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                    Application.Current.Resources["CardBrush"] = Brushes.White;
                    Application.Current.Resources["CardBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                    Application.Current.Resources["TextPrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    Application.Current.Resources["TextSecondaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                }

                // 4. Yazı Tipi (Font Family)
                if (!string.IsNullOrWhiteSpace(settings.AppFontFamily))
                {
                    Application.Current.Resources["AppFontFamily"] = new FontFamily(settings.AppFontFamily);
                }

                // 5. Köşe Oval / Keskinlik (Corner Radius)
                if (settings.CornerRadiusOption == "Düz (Keskin)")
                {
                    Application.Current.Resources["CardCornerRadius"] = new CornerRadius(0);
                    Application.Current.Resources["ButtonCornerRadius"] = new CornerRadius(2);
                }
                else if (settings.CornerRadiusOption == "Sade (Hafif)")
                {
                    Application.Current.Resources["CardCornerRadius"] = new CornerRadius(6);
                    Application.Current.Resources["ButtonCornerRadius"] = new CornerRadius(4);
                }
                else
                {
                    // Modern Oval
                    Application.Current.Resources["CardCornerRadius"] = new CornerRadius(12);
                    Application.Current.Resources["ButtonCornerRadius"] = new CornerRadius(8);
                }
            }
        }
        catch { }
    }
}
