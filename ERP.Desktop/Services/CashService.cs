using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class CashService
{
    private readonly DatabaseService _dbService;
    private readonly SalesService _salesService;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string SessionsFile = Path.Combine(StorageDirectory, "cash_sessions.json");
    private static readonly string TransactionsFile = Path.Combine(StorageDirectory, "cash_transactions.json");

    private readonly FinancialTransactionCoordinator _coordinator;

    public CashService(DatabaseService? dbService = null, SalesService? salesService = null, FinancialTransactionCoordinator? coordinator = null)
    {
        _dbService = dbService ?? new DatabaseService();
        _salesService = salesService ?? new SalesService(_dbService);
        _coordinator = coordinator ?? new FinancialTransactionCoordinator(_dbService);
        EnsureStorageDirectory();
    }

    private static void EnsureStorageDirectory()
    {
        try
        {
            if (!Directory.Exists(StorageDirectory))
            {
                Directory.CreateDirectory(StorageDirectory);
            }
        }
        catch { }
    }

    public async Task<CashSession?> GetTodaySessionAsync(DateTime? targetDate = null)
    {
        var date = (targetDate ?? DateTime.Today).Date;
        var sessions = await LoadSessionsAsync();
        return sessions.FirstOrDefault(s => s.SessionDate.Date == date && s.Status == "Açık");
    }

    public async Task<CashSession?> GetActiveSessionAsync()
    {
        var sessions = await LoadSessionsAsync();
        return sessions.FirstOrDefault(s => s.Status == "Açık");
    }

    public async Task<CashSession?> GetLatestSessionAsync(DateTime? targetDate = null)
    {
        var date = (targetDate ?? DateTime.Today).Date;
        var sessions = await LoadSessionsAsync();
        return sessions.FirstOrDefault(s => s.SessionDate.Date == date && s.Status == "Açık") 
               ?? sessions.FirstOrDefault(s => s.SessionDate.Date == date);
    }

    /// <summary>
    /// Kasadaki toplam nakit tutarı merkezi koordinatör ve hareket kayıtları üzerinden getirir.
    /// </summary>
    public async Task<decimal> GetTotalCashBalanceAsync()
    {
        return await _coordinator.GetCentralCashBalanceAsync();
    }

    public async Task<decimal> GetTotalCashBalanceFallbackAsync()
    {
        try
        {
            var startDate = ThemeService.Current?.CashBalanceStartDate ?? new DateTime(2026, 10, 8);
            var transactions = await LoadTransactionsAsync();
            decimal balance = 0;
            foreach (var t in transactions.Where(x => x.CreatedAt.Date >= startDate.Date))
            {
                if (t.IsNegative)
                    balance -= Math.Abs(t.Amount);
                else
                    balance += Math.Abs(t.Amount);
            }
            return balance;
        }
        catch
        {
            return 0m;
        }
    }

    public async Task<CashSession> OpenCashRegisterAsync(decimal openingCash, string? notes = null)
    {
        var sessions = await LoadSessionsAsync();
        var existing = sessions.FirstOrDefault(s => s.SessionDate.Date == DateTime.Today && s.Status == "Açık");

        if (existing != null)
        {
            existing.OpeningCash = openingCash;
            existing.Notes = notes;
            await SaveSessionsAsync(sessions);
            return existing;
        }

        var newSession = new CashSession
        {
            Id = sessions.Count > 0 ? sessions.Max(s => s.Id) + 1 : 1,
            SessionDate = DateTime.Today,
            OpenedAt = DateTime.Now,
            OpeningCash = openingCash,
            Status = "Açık",
            Notes = notes
        };

        sessions.Insert(0, newSession);
        await SaveSessionsAsync(sessions);

        // Kasa açılışı sisteme artı para SOKMAZ. Devreden bakiye olarak gün başlangıcı snapshot'ıdır.
        return newSession;
    }

    public async Task AddTransactionAsync(string type, decimal amount, string description, long? sessionId = null, string? sourceDocumentType = null, long? sourceDocumentId = null)
    {
        var transactions = await LoadTransactionsAsync();
        long sid = sessionId ?? (await GetTodaySessionAsync())?.Id ?? 1;

        var tx = new CashTransaction
        {
            Id = transactions.Count > 0 ? transactions.Max(t => t.Id) + 1 : 1,
            SessionId = sid,
            CreatedAt = DateTime.Now,
            Type = type,
            Amount = amount,
            Description = description,
            SourceDocumentType = sourceDocumentType,
            SourceDocumentId = sourceDocumentId
        };

        transactions.Insert(0, tx);
        await SaveTransactionsAsync(transactions);

        // Kasa bakiyesini merkezi olarak güncelle
        await GetTotalCashBalanceAsync();
    }

    public async Task RecordExpenseAsync(decimal amount, string description, DateTime date)
    {
        if (await _dbService.TestConnectionAsync())
        {
            try
            {
                await using var conn = new SqlConnection(_dbService.ConnectionString);
                await conn.OpenAsync();
                var sql = "INSERT INTO Expenses (Category, Amount, Description, ExpenseDate) VALUES (@cat, @amt, @desc, @date)";
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@cat", "Genel Masraf");
                cmd.Parameters.AddWithValue("@amt", amount);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@date", date);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }
    }

    public async Task<CashSummary> GetCashSummaryAsync(DateTime? targetDate = null)
    {
        var date = (targetDate ?? DateTime.Today).Date;
        var session = await GetTodaySessionAsync(date);

        decimal totalVault = await GetTotalCashBalanceAsync();

        if (session == null || session.Status != "Açık")
        {
            return new CashSummary
            {
                IsOpen = false,
                OpenedAt = null,
                OpeningCash = 0m,
                CashSales = 0m,
                CashIn = 0m,
                CashOut = 0m,
                TotalVaultBalance = totalVault
            };
        }

        var summary = new CashSummary
        {
            IsOpen = true,
            OpenedAt = session.OpenedAt,
            OpeningCash = session.OpeningCash,
            TotalVaultBalance = totalVault
        };

        // Oturum açılış saatinden sonraki / bugünün nakit satışları
        var sales = await _salesService.GetSalesForDateAsync(date);
        summary.CashSales = sales
            .Where(s => s.SaleDate >= session.OpenedAt)
            .Sum(s => s.GetEffectiveCashAmount());

        // Manuel kasa hareketleri (Kasa Açılışı hariç)
        var transactions = await LoadTransactionsAsync();
        var sessionTransactions = transactions
            .Where(t => t.CreatedAt.Date == date && t.CreatedAt >= session.OpenedAt && t.Type != "Kasa Açılışı")
            .ToList();

        summary.CashIn = sessionTransactions.Where(t => t.Type == "Nakit Giriş" || t.Type == "Bankadan Transfer").Sum(t => t.Amount);
        summary.CashOut = sessionTransactions.Where(t => t.Type == "Nakit Çıkış" || t.Type == "Masraf" || t.Type.Contains("Gider") || t.Type == "Bankaya Transfer").Sum(t => t.Amount);

        return summary;
    }

    public async Task<List<CashTransaction>> GetDayLedgerAsync(DateTime? targetDate = null)
    {
        var date = (targetDate ?? DateTime.Today).Date;
        var list = new List<CashTransaction>();

        // 1. Manuel kasa hareketleri (Açılış hariç - Giriş, Çıkış, Masraf, Transfer)
        var transactions = await LoadTransactionsAsync();
        var dayTransactions = transactions.Where(t => t.CreatedAt.Date == date && t.Type != "Kasa Açılışı");
        list.AddRange(dayTransactions);

        // 2. Günün nakit veya parçalı satışları (nakit kısmı)
        var sales = await _salesService.GetSalesForDateAsync(date);
        foreach (var s in sales.Where(s => s.GetEffectiveCashAmount() > 0))
        {
            list.Add(new CashTransaction
            {
                Id = 100000 + s.Id,
                CreatedAt = s.SaleDate,
                Type = s.PaymentType == "Parçalı" ? "Parçalı Satış (Nakit Kısmı)" : "Nakit Satış",
                Amount = s.GetEffectiveCashAmount(),
                Description = $"Satış/Fiş #{s.Id} ({s.Items.Count} Çeşit Ürün)" +
                              (s.PaymentType == "Parçalı" ? $" (Toplam: ₺{s.GrandTotal:N2}, Kart: ₺{s.CardAmount:N2})" : "")
            });
        }

        // 3. Günün açılış oturumu varsa, devir bilgisi olarak ekle
        var session = await GetTodaySessionAsync(date) ?? await GetLatestSessionAsync(date);
        if (session != null && session.OpeningCash > 0)
        {
            list.Add(new CashTransaction
            {
                Id = 1,
                CreatedAt = session.OpenedAt,
                Type = "Devir (Açılış)",
                Amount = session.OpeningCash,
                Description = "Günün Devreden Başlangıç Nakiti (Kasa Açılışı)"
            });
        }

        return list.OrderByDescending(t => t.CreatedAt).ToList();
    }

    private async Task<List<CashSession>> LoadSessionsAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<CashSession>();
                await using var conn = new SqlConnection(_dbService.ConnectionString);
                await conn.OpenAsync();
                var sql = "SELECT Id, SessionDate, OpenedAt, OpeningCash, ClosedAt, Status, Notes FROM CashSessions ORDER BY SessionDate DESC, Id DESC";
                await using var cmd = new SqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new CashSession
                    {
                        Id = reader.GetInt64(0),
                        SessionDate = reader.GetDateTime(1),
                        OpenedAt = reader.GetDateTime(2),
                        OpeningCash = reader.GetDecimal(3),
                        ClosedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                        Status = reader.GetString(5),
                        Notes = reader.IsDBNull(6) ? null : reader.GetString(6)
                    });
                }
                if (list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch { }

        try
        {
            if (File.Exists(SessionsFile))
            {
                var json = await File.ReadAllTextAsync(SessionsFile);
                var list = JsonSerializer.Deserialize<List<CashSession>>(json);
                if (list != null) return list;
            }
        }
        catch { }

        return new List<CashSession>();
    }

    private async Task SaveSessionsAsync(List<CashSession> sessions)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = new SqlConnection(_dbService.ConnectionString);
                await conn.OpenAsync();
                foreach (var s in sessions)
                {
                    var sql = @"
                        IF EXISTS (SELECT 1 FROM CashSessions WHERE Id = @id)
                            UPDATE CashSessions SET SessionDate=@date, OpenedAt=@opened, OpeningCash=@cash, ClosedAt=@closed, Status=@stat, Notes=@notes WHERE Id=@id
                        ELSE
                        BEGIN
                            SET IDENTITY_INSERT CashSessions ON;
                            INSERT INTO CashSessions (Id, SessionDate, OpenedAt, OpeningCash, ClosedAt, Status, Notes)
                            VALUES (@id, @date, @opened, @cash, @closed, @stat, @notes);
                            SET IDENTITY_INSERT CashSessions OFF;
                        END";
                    await using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@id", s.Id);
                    cmd.Parameters.AddWithValue("@date", s.SessionDate.Date);
                    cmd.Parameters.AddWithValue("@opened", s.OpenedAt);
                    cmd.Parameters.AddWithValue("@cash", s.OpeningCash);
                    cmd.Parameters.AddWithValue("@closed", (object?)s.ClosedAt ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@stat", s.Status);
                    cmd.Parameters.AddWithValue("@notes", (object?)s.Notes ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch { }

        try
        {
            EnsureStorageDirectory();
            var json = JsonSerializer.Serialize(sessions, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SessionsFile, json);
        }
        catch { }
    }

    private async Task<List<CashTransaction>> LoadTransactionsAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<CashTransaction>();
                await using var conn = new SqlConnection(_dbService.ConnectionString);
                await conn.OpenAsync();
                var sql = "SELECT Id, SessionId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId FROM CashTransactions ORDER BY CreatedAt DESC";
                await using var cmd = new SqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new CashTransaction
                    {
                        Id = reader.GetInt64(0),
                        SessionId = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                        Type = reader.GetString(2),
                        Amount = reader.GetDecimal(3),
                        Description = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        CreatedAt = reader.GetDateTime(5),
                        SourceDocumentType = reader.IsDBNull(6) ? null : reader.GetString(6),
                        SourceDocumentId = reader.IsDBNull(7) ? null : reader.GetInt64(7)
                    });
                }
                if (list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch { }

        try
        {
            if (File.Exists(TransactionsFile))
            {
                var json = await File.ReadAllTextAsync(TransactionsFile);
                var list = JsonSerializer.Deserialize<List<CashTransaction>>(json);
                if (list != null) return list;
            }
        }
        catch { }

        return new List<CashTransaction>();
    }

    private async Task SaveTransactionsAsync(List<CashTransaction> transactions)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = new SqlConnection(_dbService.ConnectionString);
                await conn.OpenAsync();
                foreach (var t in transactions)
                {
                    var sql = @"
                        IF NOT EXISTS (SELECT 1 FROM CashTransactions WHERE Id = @id)
                        BEGIN
                            SET IDENTITY_INSERT CashTransactions ON;
                            INSERT INTO CashTransactions (Id, SessionId, CashAccountId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId)
                            VALUES (@id, @sid, 1, @type, @amt, @desc, @created, @sdt, @sdid);
                            SET IDENTITY_INSERT CashTransactions OFF;
                        END";
                    await using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@id", t.Id);
                    cmd.Parameters.AddWithValue("@sid", (object?)t.SessionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@type", t.Type);
                    cmd.Parameters.AddWithValue("@amt", t.Amount);
                    cmd.Parameters.AddWithValue("@desc", (object?)t.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@created", t.CreatedAt);
                    cmd.Parameters.AddWithValue("@sdt", (object?)t.SourceDocumentType ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@sdid", (object?)t.SourceDocumentId ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch { }

        try
        {
            EnsureStorageDirectory();
            var json = JsonSerializer.Serialize(transactions, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(TransactionsFile, json);
        }
        catch { }
    }
    public async Task CloseTodaySessionAsync(DateTime? date = null, DateTime? closedAt = null)
    {
        var targetDate = (date ?? DateTime.Today).Date;
        var closeTime = closedAt ?? DateTime.Now;
        var sessions = await LoadSessionsAsync();
        
        var openSessions = sessions.Where(s => s.SessionDate.Date <= targetDate && s.Status == "Açık").ToList();
        foreach (var s in openSessions)
        {
            s.Status = "Kapalı";
            s.ClosedAt = closeTime;
        }

        if (openSessions.Count > 0)
        {
            await SaveSessionsAsync(sessions);
        }
        else
        {
            var latest = sessions.FirstOrDefault(s => s.SessionDate.Date == targetDate);
            if (latest != null && latest.Status != "Kapalı")
            {
                latest.Status = "Kapalı";
                latest.ClosedAt = closeTime;
                await SaveSessionsAsync(sessions);
            }
        }
    }

}
