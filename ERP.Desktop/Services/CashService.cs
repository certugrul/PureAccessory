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
        
        // 1. Önce tam tarih eşleşmesi olan açık oturumu ara
        var session = sessions.FirstOrDefault(s => s.SessionDate.Date == date && s.Status == "Açık");
        if (session != null) return session;

        // 2. Eğer targetDate belirtilmemişse (yani günün aktif kasası soruluyorsa) ve kapatılmamış açık oturum varsa onu dön
        if (targetDate == null)
        {
            var active = sessions.FirstOrDefault(s => s.Status == "Açık");
            if (active != null) return active;
        }

        return null;
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
               ?? sessions.FirstOrDefault(s => s.Status == "Açık")
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
        CashSession activeSession;

        if (existing != null)
        {
            existing.OpeningCash = openingCash;
            existing.Notes = notes;
            await SaveSessionsAsync(sessions);
            activeSession = existing;
        }
        else
        {
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
            activeSession = newSession;
        }

        // 2. Kasa Bakiyesini hedef openingCash değerine anında eşitle
        try
        {
            var currentVault = await _coordinator.GetCentralCashBalanceAsync();
            var diff = openingCash - currentVault;
            if (diff != 0)
            {
                var adjType = diff > 0 ? "Kasa Düzeltme (+)" : "Kasa Düzeltme (-)";
                var adjAmount = Math.Abs(diff);
                var adjDesc = $"Kasa Nakit Bakiyesi Ayarlandı (Hedef: ₺{openingCash:N2})";

                await AddTransactionAsync(adjType, adjAmount, adjDesc, activeSession.Id, "ADJUSTMENT", activeSession.Id);
            }

            // Doğrudan CashAccounts (Id = 1) tablosunu da DB'de garanti altına al
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var updAccSql = "UPDATE CashAccounts SET Balance = @bal WHERE Id = 1;";
                await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(updAccSql));
                cmd.AddParam("bal", openingCash);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CashService] Kasa bakiyesi eşitleme hatası: {ex.Message}");
        }

        // Önbellekteki oturumu ve bakiyeyi anında güncelle
        AppDataCache.UpdateTodayCashSession(activeSession);
        AppDataCache.UpdateCashState(null, openingCash, activeSession);

        return activeSession;
    }

    private static readonly SemaphoreSlim _cashLock = new(1, 1);
    private static string? _lastCashFingerprint;
    private static DateTime _lastCashTime = DateTime.MinValue;

    public async Task AddTransactionAsync(string type, decimal amount, string description, long? sessionId = null, string? sourceDocumentType = null, long? sourceDocumentId = null)
    {
        var fp = $"{type}|{amount:F2}|{description}|{sessionId}|{sourceDocumentType}|{sourceDocumentId}";
        await _cashLock.WaitAsync();
        try
        {
            if (_lastCashFingerprint == fp && (DateTime.Now - _lastCashTime).TotalSeconds < 2.5)
            {
                System.Diagnostics.Debug.WriteLine($"[CashService] Mükerrer kasa hareketi engellendi: {fp}");
                return;
            }

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

            // 1. Doğrudan veritabanına tekil sorguyla anında yaz
            if (await _dbService.TestConnectionAsync())
            {
                try
                {
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();
                    var insertSql = @"
                        INSERT INTO CashTransactions (SessionId, CashAccountId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId)
                        VALUES (@sid, 1, @type, @amt, @desc, @created, @sdt, @sdid);
                        SELECT SCOPE_IDENTITY();";
                    await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(insertSql));
                    cmd.AddParam("sid", tx.SessionId);
                    cmd.AddParam("type", tx.Type);
                    cmd.AddParam("amt", tx.Amount);
                    cmd.AddParam("desc", tx.Description);
                    cmd.AddParam("created", tx.CreatedAt);
                    cmd.AddParam("sdt", tx.SourceDocumentType);
                    cmd.AddParam("sdid", tx.SourceDocumentId);
                    var scalar = await cmd.ExecuteScalarAsync();
                    if (scalar != null && scalar != DBNull.Value)
                    {
                        tx.Id = Convert.ToInt64(scalar);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CashService] DB Kasa hareketi ekleme hatası: {ex.Message}");
                }
            }

            // 2. Yerel JSON'a ekle
            transactions.Insert(0, tx);
            try
            {
                EnsureStorageDirectory();
                var json = JsonSerializer.Serialize(transactions, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(TransactionsFile, json);
            }
            catch { }

            _lastCashFingerprint = fp;
            _lastCashTime = DateTime.Now;

            // Kasa bakiyesini merkezi olarak güncelle
            var newBalance = await GetTotalCashBalanceAsync();
            AppDataCache.UpdateCashState(null, newBalance);
        }
        finally
        {
            _cashLock.Release();
        }
    }

    public async Task RecordExpenseAsync(decimal amount, string description, DateTime date)
    {
        if (await _dbService.TestConnectionAsync())
        {
            try
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = "INSERT INTO Expenses (Category, Amount, Description, ExpenseDate) VALUES (@cat, @amt, @desc, @date);";
                await using var cmd = conn.CreateCommand(sql);
                cmd.AddParam("cat", "Genel Masraf");
                cmd.AddParam("amt", amount);
                cmd.AddParam("desc", description);
                cmd.AddParam("date", date);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }
    }

    public async Task<CashSummary> GetCashSummaryAsync(DateTime? targetDate = null)
    {
        var date = (targetDate ?? DateTime.Today).Date;
        var nextDate = date.AddDays(1);

        var sessionTask = GetTodaySessionAsync(date);
        var totalVaultTask = GetTotalCashBalanceAsync();
        var salesTask = _salesService.GetSalesForDateAsync(date);
        var transactionsTask = LoadTransactionsAsync(date, nextDate);

        await Task.WhenAll(sessionTask, totalVaultTask, salesTask, transactionsTask);

        var session = await sessionTask;
        decimal totalVault = await totalVaultTask;

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
        var sales = await salesTask;
        summary.CashSales = sales
            .Where(s => s.SaleDate >= session.OpenedAt)
            .Sum(s => s.GetEffectiveCashAmount());

        // Manuel kasa hareketleri (Kasa Açılışı hariç)
        var transactions = await transactionsTask;
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
        var nextDate = date.AddDays(1);
        var list = new List<CashTransaction>();

        var transactionsTask = LoadTransactionsAsync(date, nextDate);
        var salesTask = _salesService.GetSalesForDateAsync(date);
        var sessionTask = GetTodaySessionAsync(date);

        await Task.WhenAll(transactionsTask, salesTask, sessionTask);

        // 1. Manuel kasa hareketleri (Açılış hariç - Giriş, Çıkış, Masraf, Transfer)
        var transactions = await transactionsTask;
        var dayTransactions = transactions.Where(t => t.CreatedAt.Date == date && t.Type != "Kasa Açılışı");
        list.AddRange(dayTransactions);

        // 2. Günün nakit veya parçalı satışları (nakit kısmı)
        var sales = await salesTask;
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
        var session = (await sessionTask) ?? await GetLatestSessionAsync(date);
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
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = "SELECT Id, SessionDate, OpenedAt, OpeningCash, ClosedAt, Status, Notes FROM CashSessions ORDER BY SessionDate DESC, Id DESC";
                await using var cmd = conn.CreateCommand(sql);
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
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var s in sessions)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM CashSessions WHERE Id = @id");
                    checkCmd.AddParam("id", s.Id);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (exists)
                    {
                        var updateSql = @"UPDATE CashSessions SET SessionDate=@date, OpenedAt=@opened, OpeningCash=@cash, ClosedAt=@closed, Status=@stat, Notes=@notes WHERE Id=@id";
                        await using var cmd = conn.CreateCommand(updateSql);
                        cmd.AddParam("id", s.Id);
                        cmd.AddParam("date", s.SessionDate.Date);
                        cmd.AddParam("opened", s.OpenedAt);
                        cmd.AddParam("cash", s.OpeningCash);
                        cmd.AddParam("closed", s.ClosedAt);
                        cmd.AddParam("stat", s.Status);
                        cmd.AddParam("notes", s.Notes);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var insertSql = @"INSERT INTO CashSessions (Id, SessionDate, OpenedAt, OpeningCash, ClosedAt, Status, Notes)
                                          VALUES (@id, @date, @opened, @cash, @closed, @stat, @notes);";
                        await using var cmd = conn.CreateCommand(insertSql);
                        cmd.AddParam("id", s.Id);
                        cmd.AddParam("date", s.SessionDate.Date);
                        cmd.AddParam("opened", s.OpenedAt);
                        cmd.AddParam("cash", s.OpeningCash);
                        cmd.AddParam("closed", s.ClosedAt);
                        cmd.AddParam("stat", s.Status);
                        cmd.AddParam("notes", s.Notes);
                        await cmd.ExecuteNonQueryAsync();
                    }
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

    private async Task<List<CashTransaction>> LoadTransactionsAsync(DateTime? startDate = null, DateTime? endDate = null, int? limit = null)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<CashTransaction>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                string sql;
                if (startDate.HasValue && endDate.HasValue)
                {
                    sql = "SELECT Id, SessionId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId FROM CashTransactions WHERE CreatedAt >= @StartDate AND CreatedAt < @EndDate ORDER BY CreatedAt DESC";
                }
                else if (startDate.HasValue)
                {
                    sql = "SELECT Id, SessionId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId FROM CashTransactions WHERE CreatedAt >= @StartDate ORDER BY CreatedAt DESC";
                }
                else
                {
                    var lim = limit ?? 500;
                    sql = $"SELECT TOP {lim} Id, SessionId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId FROM CashTransactions ORDER BY CreatedAt DESC";
                }

                await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(sql));
                if (startDate.HasValue) cmd.AddParam("StartDate", startDate.Value);
                if (endDate.HasValue) cmd.AddParam("EndDate", endDate.Value);

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
                if (list != null)
                {
                    if (startDate.HasValue && endDate.HasValue)
                        return list.Where(t => t.CreatedAt >= startDate.Value && t.CreatedAt < endDate.Value).ToList();
                    if (startDate.HasValue)
                        return list.Where(t => t.CreatedAt >= startDate.Value).ToList();
                    return list;
                }
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
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var t in transactions)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM CashTransactions WHERE Id = @id");
                    checkCmd.AddParam("id", t.Id);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (!exists)
                    {
                        var insertSql = @"INSERT INTO CashTransactions (Id, SessionId, CashAccountId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId)
                                          VALUES (@id, @sid, 1, @type, @amt, @desc, @created, @sdt, @sdid);";
                        await using var cmd = conn.CreateCommand(insertSql);
                        cmd.AddParam("id", t.Id);
                        cmd.AddParam("sid", t.SessionId);
                        cmd.AddParam("type", t.Type);
                        cmd.AddParam("amt", t.Amount);
                        cmd.AddParam("desc", t.Description);
                        cmd.AddParam("created", t.CreatedAt);
                        cmd.AddParam("sdt", t.SourceDocumentType);
                        cmd.AddParam("sdid", t.SourceDocumentId);
                        await cmd.ExecuteNonQueryAsync();
                    }
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
            AppDataCache.UpdateTodayCashSession(null);
        }
        else
        {
            var latest = sessions.FirstOrDefault(s => s.SessionDate.Date == targetDate);
            if (latest != null && latest.Status != "Kapalı")
            {
                latest.Status = "Kapalı";
                latest.ClosedAt = closeTime;
                await SaveSessionsAsync(sessions);
                AppDataCache.UpdateTodayCashSession(null);
            }
        }
    }

}
