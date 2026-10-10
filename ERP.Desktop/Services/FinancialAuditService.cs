using System.Data.Common;
using ERP.Desktop.Models;

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
        DbConnection? existingConn = null,
        DbTransaction? existingTran = null)
    {
        bool closeConn = false;
        var conn = existingConn;
        if (conn == null)
        {
            conn = _dbService.CreateConnection();
            await conn.OpenAsync();
            closeConn = true;
        }

        try
        {
            var sql = @"
                INSERT INTO FinancialAuditLogs (ActionType, EntityName, EntityId, BankAccountId, DebtId, OldValue, NewValue, Description, UserId, UserName, CreatedAt)
                VALUES (@act, @ent, @entId, @baid, @did, @old, @new, @desc, @uid, @uname, CURRENT_TIMESTAMP);";

            await using var cmd = conn.CreateCommand(sql, existingTran);
            cmd.AddParam("act", actionType);
            cmd.AddParam("ent", entityName);
            cmd.AddParam("entId", entityId);
            cmd.AddParam("baid", bankAccountId);
            cmd.AddParam("did", debtId);
            cmd.AddParam("old", oldValue);
            cmd.AddParam("new", newValue);
            cmd.AddParam("desc", description);
            cmd.AddParam("uid", userId);
            cmd.AddParam("uname", userName);

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
