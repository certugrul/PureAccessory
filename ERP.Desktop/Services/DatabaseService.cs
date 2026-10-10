using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class DatabaseService
{
    private static string? _detectedWorkingConnectionString;
    private string _defaultConnectionString = "Server=.\\SQLEXPRESS;Database=ERPSystem;Trusted_Connection=True;TrustServerCertificate=True;";
    private readonly string? _forcedConnectionString;

    public DatabaseService(string? customConnectionString = null)
    {
        _forcedConnectionString = customConnectionString;
        if (!string.IsNullOrWhiteSpace(customConnectionString))
        {
            _defaultConnectionString = customConnectionString;
        }
    }

    public string ConnectionString
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_forcedConnectionString))
                return _forcedConnectionString;

            var custom = ThemeService.Current?.CustomDbConnectionString;
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            return _detectedWorkingConnectionString ?? _defaultConnectionString;
        }
        set => _defaultConnectionString = value;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString)
            {
                ConnectTimeout = 3
            };
            await using var c = new SqlConnection(builder.ConnectionString);
            await c.OpenAsync();
            return true;
        }
        catch
        {
            // Eğer doğrudan bağlanamadıysa, yerel SQL Server ve Express sürümlerini tara ve ERPSystem veritabanını oluştur
            return await EnsureDatabaseExistsAndDetectAsync();
        }
    }

    public async Task<bool> EnsureDatabaseExistsAndDetectAsync()
    {
        var candidates = new List<string>
        {
            @".\SQLEXPRESS",
            @"localhost\SQLEXPRESS",
            $@"{Environment.MachineName}\SQLEXPRESS",
            @"(local)\SQLEXPRESS",
            "localhost",
            ".",
            "(local)",
            @"(localdb)\MSSQLLocalDB"
        };

        foreach (var server in candidates)
        {
            var masterConnStr = $"Server={server};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3;";
            try
            {
                await using var conn = new SqlConnection(masterConnStr);
                await conn.OpenAsync();

                // ERPSystem veritabanı var mı kontrol et, yoksa oluştur
                var checkSql = @"IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'ERPSystem')
                                 BEGIN
                                     CREATE DATABASE ERPSystem;
                                 END";
                await using var cmd = new SqlCommand(checkSql, conn);
                await cmd.ExecuteNonQueryAsync();

                _detectedWorkingConnectionString = $"Server={server};Database=ERPSystem;Trusted_Connection=True;TrustServerCertificate=True;";
                _defaultConnectionString = _detectedWorkingConnectionString;

                // Ayarlara da kalıcı olarak kaydet
                try
                {
                    if (ThemeService.Current != null)
                    {
                        ThemeService.Current.CustomDbConnectionString = _detectedWorkingConnectionString;
                        ThemeService.Save(ThemeService.Current);
                    }
                }
                catch { }

                // Tabloları hemen oluştur
                try
                {
                    var syncService = new DatabaseSyncService(this);
                    await syncService.EnsureTablesCreatedAsync();
                }
                catch { }

                return true;
            }
            catch
            {
                // Bir sonraki adayı dene
            }
        }

        return false;
    }
}
