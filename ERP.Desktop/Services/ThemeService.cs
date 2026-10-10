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
                if (!string.IsNullOrWhiteSpace(settings.PrimaryColor))
                {
                    var color = (Color)ColorConverter.ConvertFromString(settings.PrimaryColor);
                    Application.Current.Resources["PrimaryBrush"] = new SolidColorBrush(color);
                }

                if (!string.IsNullOrWhiteSpace(settings.SidebarColor))
                {
                    var color = (Color)ColorConverter.ConvertFromString(settings.SidebarColor);
                    Application.Current.Resources["SidebarBrush"] = new SolidColorBrush(color);
                }

                if (!string.IsNullOrWhiteSpace(settings.PageBackgroundColor))
                {
                    var color = (Color)ColorConverter.ConvertFromString(settings.PageBackgroundColor);
                    Application.Current.Resources["PageBrush"] = new SolidColorBrush(color);
                }
            }
        }
        catch { }
    }
}
