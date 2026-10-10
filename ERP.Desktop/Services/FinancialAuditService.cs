using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class FinancialAuditService
{
    private readonly DatabaseService _dbService;

    public FinancialAuditService(DatabaseService? dbService = null)
    {
        _dbService = dbService ?? new DatabaseService();
    }

    public async Task LogAsync(
        string actionType,
        string entityName,
        long? entityId,
        string description,
        string? oldValue = null,
        string? newValue = null,
        int? bankAccountId = null,
        int? debtId = null,
        int? userId = null,
        string userName = "Sistem",
        SqlConnection? existingConn = null,
        SqlTransaction? existingTran = null)
    {
        bool closeConn = false;
        var conn = existingConn;
        if (conn == null)
        {
            conn = new SqlConnection(_dbService.ConnectionString);
            await conn.OpenAsync();
            closeConn = true;
        }

        try
        {
            var sql = @"
                INSERT INTO FinancialAuditLogs (ActionType, EntityName, EntityId, BankAccountId, DebtId, OldValue, NewValue, Description, UserId, UserName, CreatedAt)
                VALUES (@act, @ent, @entId, @baid, @did, @old, @new, @desc, @uid, @uname, GETDATE());";

            await using var cmd = new SqlCommand(sql, conn, existingTran);
            cmd.Parameters.AddWithValue("@act", actionType);
            cmd.Parameters.AddWithValue("@ent", entityName);
            cmd.Parameters.AddWithValue("@entId", (object?)entityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@baid", (object?)bankAccountId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@did", (object?)debtId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@old", (object?)oldValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@new", (object?)newValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@desc", description);
            cmd.Parameters.AddWithValue("@uid", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@uname", userName);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FinancialAuditService] Log error: {ex.Message}");
        }
        finally
        {
            if (closeConn && conn != null)
            {
                await conn.DisposeAsync();
            }
        }
    }
}
