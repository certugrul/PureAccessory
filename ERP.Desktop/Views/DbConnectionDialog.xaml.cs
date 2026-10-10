using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class DbConnectionDialog : Window
{
    private readonly DatabaseService _dbService;

    public DbConnectionDialog()
    {
        InitializeComponent();
        _dbService = new DatabaseService();

        Loaded += DbConnectionDialog_Loaded;
    }

    private async void DbConnectionDialog_Loaded(object sender, RoutedEventArgs e)
    {
        var currentConn = ThemeService.Current?.CustomDbConnectionString;
        if (string.IsNullOrWhiteSpace(currentConn))
        {
            currentConn = _dbService.ConnectionString;
        }

        TxtConnStr.Text = currentConn;
        await RunTestAsync(currentConn);
    }

    private void UseSqlExpress_Click(object sender, RoutedEventArgs e)
    {
        TxtConnStr.Text = "Server=.\\SQLEXPRESS;Database=ERPSystem;Trusted_Connection=True;TrustServerCertificate=True;";
    }

    private void UseLocalhost_Click(object sender, RoutedEventArgs e)
    {
        TxtConnStr.Text = "Server=localhost;Database=ERPSystem;Trusted_Connection=True;TrustServerCertificate=True;";
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var connStr = TxtConnStr.Text.Trim();
        await RunTestAsync(connStr);
    }

    private async Task<bool> RunTestAsync(string connStr)
    {
        SetStatusState("Kontrol ediliyor...", "#EAB308", "SQL Server ile bağlantı kuruluyor...");

        try
        {
            var testService = new DatabaseService(string.IsNullOrWhiteSpace(connStr) ? null : connStr);
            bool ok = await testService.TestConnectionAsync();

            if (ok)
            {
                SetStatusState("Bağlantı Başarılı!", "#10B981", $"SQL Server aktif ve ERPSystem veritabanına erişildi.");
                return true;
            }
            else
            {
                SetStatusState("Bağlantı Başarısız!", "#EF4444", "Sunucuya bağlanılamadı. SQL Server servisinin çalıştığından emin olun.");
                return false;
            }
        }
        catch (Exception ex)
        {
            SetStatusState("Hata!", "#EF4444", ex.Message);
            return false;
        }
    }

    private async void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        SetStatusState("Taranıyor...", "#EAB308", "Yerel SQL Server Express ve Standart örnekleri taranıyor...");

        var detectService = new DatabaseService();
        bool found = await detectService.EnsureDatabaseExistsAndDetectAsync();

        if (found)
        {
            TxtConnStr.Text = detectService.ConnectionString;
            SetStatusState("Algılandı ve Bağlandı!", "#10B981", $"Aktif sunucu: {detectService.ConnectionString}");
        }
        else
        {
            SetStatusState("Sunucu Bulunamadı", "#EF4444", "Yerel SQL Server Express servisi bulunamadı veya çalışmıyor.");
        }
    }

    private void SetStatusState(string title, string hexColor, string detail)
    {
        try
        {
            DotStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            TxtStatusTitle.Text = title;
            TxtStatusDetail.Text = detail;
        }
        catch { }
    }

    private async void SaveAndApply_Click(object sender, RoutedEventArgs e)
    {
        var connStr = TxtConnStr.Text.Trim();

        // Ayarları güncelle
        var settings = ThemeService.Current ?? new AppSettings();
        settings.CustomDbConnectionString = string.IsNullOrWhiteSpace(connStr) ? null : connStr;
        ThemeService.Save(settings);

        // Tabloları hemen garantiye al
        try
        {
            var syncService = new DatabaseSyncService(new DatabaseService(settings.CustomDbConnectionString));
            await syncService.EnsureTablesCreatedAsync();
        }
        catch { }

        DialogResult = true;
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
