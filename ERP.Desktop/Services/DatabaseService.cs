using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace ERP.Desktop.Services;

/// <summary>
/// Veritabanı sağlayıcı türü
/// </summary>
public enum DatabaseProviderType
{
    PostgreSql, // Supabase / Cloud PostgreSQL
    SqlServer   // Yerel veya Uzak Microsoft SQL Server
}

/// <summary>
/// Hem Supabase PostgreSQL hem de MS SQL Server ile kusursuz çift yönlü çalışan
/// merkezi veritabanı bağlantı ve sorgu yöneticisi.
/// </summary>
public class DatabaseService
{
    // Supabase PostgreSQL Canlı Bağlantı Bilgisi (Bulut Veritabanı)
    public const string DefaultSupabaseConnectionString =
        "Host=aws-0-eu-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.mcmjhoseokngnryzmszo;Password=certugrulERP;SSL Mode=Require;Trust Server Certificate=true;Timeout=30;Command Timeout=60;";

    // Yerel SQL Server Fallback
    public const string DefaultSqlServerConnectionString =
        "Server=localhost;Database=ERPSystem;Integrated Security=True;TrustServerCertificate=True;";

    private readonly string? _forcedConnectionString;
    private static string? _workingConnectionString;

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

            var custom = ThemeService.Current?.CustomDbConnectionString;
            if (!string.IsNullOrWhiteSpace(custom) && 
                !custom.Contains("localhost", StringComparison.OrdinalIgnoreCase) && 
                !custom.Contains(".\\SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
            {
                return custom;
            }

            return _workingConnectionString ?? DefaultSupabaseConnectionString;
        }
        set
        {
            _workingConnectionString = value;
        }
    }

    /// <summary>
    /// Bağlantı dizesine göre aktif veritabanı türünü belirler.
    /// </summary>
    public DatabaseProviderType ProviderType
    {
        get
        {
            var str = ConnectionString;
            if (str.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
                str.Contains("supabase.com", StringComparison.OrdinalIgnoreCase) ||
                str.Contains("Port=5432", StringComparison.OrdinalIgnoreCase) ||
                str.Contains("Port=6543", StringComparison.OrdinalIgnoreCase) ||
                str.Contains("postgres", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseProviderType.PostgreSql;
            }

            return DatabaseProviderType.SqlServer;
        }
    }

    public bool IsPostgres => ProviderType == DatabaseProviderType.PostgreSql;

    /// <summary>
    /// Yeni bir DbConnection (NpgsqlConnection veya SqlConnection) oluşturur.
    /// </summary>
    public DbConnection CreateConnection()
    {
        if (IsPostgres)
        {
            return new NpgsqlConnection(ConnectionString);
        }
        return new SqlConnection(ConnectionString);
    }

    /// <summary>
    /// Açık bir DbConnection (NpgsqlConnection veya SqlConnection) döndürür.
    /// </summary>
    public async Task<DbConnection> OpenConnectionAsync()
    {
        var conn = CreateConnection();
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// Bağlantıyı test eder. Supabase aktifse true döner; değilse yerel SQL Server fallback dener.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        // 1. Mevcut bağlantıyı dene
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] TestConnection hatası: {ex.Message}");
        }

        // 2. Eğer Supabase denendiyse ve ulaşılamadıysa yerel SQL Server'ı dene
        if (IsPostgres)
        {
            try
            {
                await using var sqlConn = new SqlConnection(DefaultSqlServerConnectionString);
                await sqlConn.OpenAsync();
                _workingConnectionString = DefaultSqlServerConnectionString;
                return true;
            }
            catch { }
        }

        // 3. Yerel SQL Server Express adaylarını tara
        return await EnsureDatabaseExistsAndDetectAsync();
    }

    /// <summary>
    /// T-SQL sorgularını PostgreSQL uyumlu hale getirir (Diyalekt dönüştürücü).
    /// </summary>
    public string NormalizeSql(string sql)
    {
        if (!IsPostgres) return sql;

        var s = sql;

        // 1. ISNULL(a, b) -> COALESCE(a, b)
        s = Regex.Replace(s, @"\bISNULL\s*\(", "COALESCE(", RegexOptions.IgnoreCase);

        // 2. GETDATE() -> CURRENT_TIMESTAMP
        s = Regex.Replace(s, @"\bGETDATE\s*\(\s*\)", "CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);

        // 3. SET IDENTITY_INSERT ... ON/OFF komutlarını kaldır (Postgres'te gerekmez)
        s = Regex.Replace(s, @"SET\s+IDENTITY_INSERT\s+[^\s;]+\s+(ON|OFF)\s*;?", "", RegexOptions.IgnoreCase);

        // 4. Unicode N'...' ön ekini kaldır -> '...'
        s = Regex.Replace(s, @"N'([^']*)'", "'$1'");

        // 5. SELECT SCOPE_IDENTITY(); -> RETURNING Id;
        // Eğer INSERT ve SELECT SCOPE_IDENTITY() bir aradaysa:
        if (Regex.IsMatch(s, @"SELECT\s+SCOPE_IDENTITY\s*\(\s*\)\s*;?", RegexOptions.IgnoreCase))
        {
            s = Regex.Replace(s, @";?\s*SELECT\s+SCOPE_IDENTITY\s*\(\s*\)\s*;?", " RETURNING Id;", RegexOptions.IgnoreCase);
        }

        // 6. TOP n -> LIMIT n
        // Örn: SELECT TOP 1 -> SELECT ... LIMIT 1
        var topMatch = Regex.Match(s, @"SELECT\s+TOP\s+(\d+)\s+(.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (topMatch.Success)
        {
            int limit = int.Parse(topMatch.Groups[1].Value);
            string remainder = topMatch.Groups[2].Value;
            s = $"SELECT {remainder} LIMIT {limit}";
        }

        // 7. Tablo adlarını küçük harfe çevir (PostgreSQL case-sensitivity)
        var tables = new[]
        {
            "Users", "Warehouses", "Products", "WarehouseStocks", "StockMovements",
            "Sales", "SaleItems", "CashSessions", "CashAccounts", "CashTransactions",
            "Expenses", "Closures", "CariAccounts", "CariTransactions", "BankAccounts",
            "BankTransactions", "PosTransactions", "Invoices", "InvoiceItems",
            "GeneralExpenses", "FinancialDebts", "FinancialReminders", "InterestRates"
        };

        foreach (var t in tables)
        {
            s = Regex.Replace(s, $@"\b{t}\b", t.ToLowerInvariant(), RegexOptions.IgnoreCase);
        }

        return s;
    }

    /// <summary>
    /// Yerel SQL Server örneklerini tarar (Fallback amacıyla korunur).
    /// </summary>
    public async Task<bool> EnsureDatabaseExistsAndDetectAsync()
    {
        var candidates = new List<string>
        {
            "localhost",
            @".\SQLEXPRESS",
            @"localhost\SQLEXPRESS",
            $@"{Environment.MachineName}\SQLEXPRESS",
            @"(local)\SQLEXPRESS",
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

                var checkSql = @"IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'ERPSystem')
                                 BEGIN
                                     CREATE DATABASE ERPSystem;
                                 END";
                await using var cmd = new SqlCommand(checkSql, conn);
                await cmd.ExecuteNonQueryAsync();

                _workingConnectionString = $"Server={server};Database=ERPSystem;Trusted_Connection=True;TrustServerCertificate=True;";
                return true;
            }
            catch { }
        }

        return false;
    }
}
