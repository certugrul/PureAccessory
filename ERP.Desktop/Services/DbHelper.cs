using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace ERP.Desktop.Services;

/// <summary>
/// Veritabanı sorgularını hem PostgreSQL (Supabase) hem de SQL Server üzerinde
/// şeffaf ve parametreli olarak yürüten yardımcı araç.
/// </summary>
public static class DbHelper
{
    public static DbCommand CreateCommand(this DbConnection connection, string sql, DbTransaction? transaction = null)
    {
        var normalizedSql = NormalizeSql(sql, connection is NpgsqlConnection);
        var cmd = connection.CreateCommand();
        cmd.CommandText = normalizedSql;
        if (transaction != null)
        {
            cmd.Transaction = transaction;
        }
        return cmd;
    }

    public static DbParameter AddParam(this DbCommand cmd, string name, object? value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name.StartsWith("@") ? name : "@" + name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
        return param;
    }

    public static DbParameter AddWithValue(this DbParameterCollection parameters, string name, object? value)
    {
        throw new NotSupportedException("Use DbCommand.AddParam instead");
    }

    public static string NormalizeSql(string sql, bool isPostgres)
    {
        if (!isPostgres) return sql;

        var s = sql;

        // 1. ISNULL(a, b) -> COALESCE(a, b)
        s = Regex.Replace(s, @"\bISNULL\s*\(", "COALESCE(", RegexOptions.IgnoreCase);

        // 2. GETDATE() -> CURRENT_TIMESTAMP
        s = Regex.Replace(s, @"\bGETDATE\s*\(\s*\)", "CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);

        // 3. SET IDENTITY_INSERT ... ON/OFF komutlarını kaldır
        s = Regex.Replace(s, @"SET\s+IDENTITY_INSERT\s+[^\s;]+\s+(ON|OFF)\s*;?", "", RegexOptions.IgnoreCase);

        // 3b. WITH (UPDLOCK, ROWLOCK, etc.) SQL Server lock hintlerini kaldır
        s = Regex.Replace(s, @"WITH\s*\([^)]*\)", "", RegexOptions.IgnoreCase);

        // 4. Unicode N'...' ön ekini kaldır -> '...'
        s = Regex.Replace(s, @"N'([^']*)'", "'$1'");

        // 5. SELECT SCOPE_IDENTITY(); -> RETURNING Id;
        if (Regex.IsMatch(s, @"SELECT\s+SCOPE_IDENTITY\s*\(\s*\)\s*;?", RegexOptions.IgnoreCase))
        {
            s = Regex.Replace(s, @";?\s*SELECT\s+SCOPE_IDENTITY\s*\(\s*\)\s*;?", " RETURNING Id;", RegexOptions.IgnoreCase);
        }

        // 5b. OUTPUT INSERTED.Id / OUTPUT INSERTED.* -> RETURNING Id / RETURNING *
        if (Regex.IsMatch(s, @"OUTPUT\s+INSERTED\.(\w+)", RegexOptions.IgnoreCase))
        {
            s = Regex.Replace(s, @"OUTPUT\s+INSERTED\.(\w+)", "", RegexOptions.IgnoreCase);
            s = s.TrimEnd(';', ' ') + " RETURNING $1;";
        }

        // 6. TOP n -> LIMIT n
        var topMatch = Regex.Match(s, @"SELECT\s+TOP\s+(\d+)\s+(.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (topMatch.Success)
        {
            int limit = int.Parse(topMatch.Groups[1].Value);
            string remainder = topMatch.Groups[2].Value;
            s = $"SELECT {remainder} LIMIT {limit}";
        }

        // 6b. "User" veya [User] sütun adını PostgreSQL için küçük harf "user" yap
        s = Regex.Replace(s, @"\""User\""", "\"user\"", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[User\]", "\"user\"", RegexOptions.IgnoreCase);

        // 6c. Boolean karşılaştırmaları (= 1 -> = true, = 0 -> = false)
        s = Regex.Replace(s, @"\b(IsActive|IsDeleted|IsDefault|IsApplied|IsRecurringMonthly|IsPaid)\s*=\s*1\b", "$1 = true", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(IsActive|IsDeleted|IsDefault|IsApplied|IsRecurringMonthly|IsPaid)\s*=\s*0\b", "$1 = false", RegexOptions.IgnoreCase);

        // 7. Tablo adlarını küçük harfe dönüştür
        var tables = new[]
        {
            "Users", "Warehouses", "Products", "WarehouseStocks", "StockMovements",
            "Sales", "SaleItems", "CashSessions", "CashAccounts", "CashTransactions",
            "Expenses", "Closures", "CariAccounts", "CariTransactions", "BankAccounts",
            "BankTransactions", "PosTransactions", "Invoices", "InvoiceItems",
            "GeneralExpenses", "FinancialDebts", "FinancialReminders", "InterestRates",
            "DebtTransactions", "InterestAccruals", "Payments", "PaymentAllocations", "FinancialAuditLogs"
        };

        foreach (var t in tables)
        {
            s = Regex.Replace(s, $@"\b{t}\b", t.ToLowerInvariant(), RegexOptions.IgnoreCase);
        }

        return s;
    }
}
