using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Npgsql;

namespace ERP.Desktop.Services;

/// <summary>
/// Veritabanı sağlayıcı türü
/// </summary>
public enum DatabaseProviderType
{
    PostgreSql // Supabase / Cloud PostgreSQL
}

/// <summary>
/// Supabase PostgreSQL bulut veritabanı bağlantı ve sorgu yöneticisi.
/// </summary>
public class DatabaseService
{
    // Supabase PostgreSQL Canlı Bağlantı Bilgisi (Bulut Veritabanı) - Sıcak Bağlantı Havuzu Optimizasyonu
    public const string DefaultSupabaseConnectionString =
        "Host=aws-0-eu-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.mcmjhoseokngnryzmszo;Password=certugrulERP;SSL Mode=Require;Trust Server Certificate=true;Timeout=30;Command Timeout=60;Pooling=true;Minimum Pool Size=3;Maximum Pool Size=30;Connection Idle Lifetime=300;KeepAlive=30;";

    private readonly string? _forcedConnectionString;
    private static string? _workingConnectionString;

    // Bağlantı doğrulama önbelleği (Gereksiz ping & SSL el sıkışmalarını önler)
    private static DateTime _lastSuccessfulConnectionTime = DateTime.MinValue;
    private static readonly TimeSpan ConnectionCacheTtl = TimeSpan.FromSeconds(30);

    public static void InvalidateConnectionCache()
    {
        _lastSuccessfulConnectionTime = DateTime.MinValue;
    }

    public static void MarkConnectionSuccessful()
    {
        _lastSuccessfulConnectionTime = DateTime.UtcNow;
    }

    public DatabaseService(string? customConnectionString = null)
    {
        _forcedConnectionString = customConnectionString;
    }

    /// <summary>
    /// Aktif bağlantı dizesi (Varsayılan olarak Supabase Bulut Veritabanı)
    /// </summary>
    public string ConnectionString
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_forcedConnectionString))
                return _forcedConnectionString;

            return _workingConnectionString ?? DefaultSupabaseConnectionString;
        }
        set
        {
            _workingConnectionString = value;
            InvalidateConnectionCache();
        }
    }

    public DatabaseProviderType ProviderType => DatabaseProviderType.PostgreSql;

    public bool IsPostgres => true;

    /// <summary>
    /// Yeni bir NpgsqlConnection oluşturur.
    /// </summary>
    public DbConnection CreateConnection()
    {
        return new NpgsqlConnection(ConnectionString);
    }

    /// <summary>
    /// Açık bir NpgsqlConnection döndürür.
    /// </summary>
    public async Task<DbConnection> OpenConnectionAsync()
    {
        var conn = CreateConnection();
        await conn.OpenAsync();
        MarkConnectionSuccessful();
        return conn;
    }

    /// <summary>
    /// Bağlantıyı test eder. Son 30 saniye içinde başarılı bağlantı kurulmuşsa ağ trafiği yaratmadan doğrudan true döner.
    /// </summary>
    public async Task<bool> TestConnectionAsync(bool force = false)
    {
        if (!force && (DateTime.UtcNow - _lastSuccessfulConnectionTime) < ConnectionCacheTtl)
        {
            return true;
        }

        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            MarkConnectionSuccessful();
            return true;
        }
        catch (Exception ex)
        {
            InvalidateConnectionCache();
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] TestConnection hatası: {ex.Message}");
            return false;
        }
    }

    public string NormalizeSql(string sql) => DbHelper.NormalizeSql(sql, true);
}
