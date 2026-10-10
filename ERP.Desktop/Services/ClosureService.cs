using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class ClosureService
{
    private readonly DatabaseService _dbService;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string StorageFile = Path.Combine(StorageDirectory, "closures.json");

    public ClosureService(DatabaseService? dbService = null)
    {
        _dbService = dbService ?? new DatabaseService();
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

    /// <summary>
    /// Dashboard ekranında gösterilecek aktif (kapanıştan sonraki) günlük metrikleri getirir.
    /// Gün sonu alınmışsa ve sonrasında yeni satış yapılmamışsa 0 döner.
    /// </summary>
    public async Task<DailyClosure> GetActiveDashboardMetricsAsync(DateTime targetDate)
    {
        var closures = await GetClosuresAsync();
        var lastClosure = closures
            .Where(c => c.ClosureDate.Date == targetDate.Date)
            .OrderByDescending(c => c.ClosedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefault();

        // Eğer o güne ait kapanış alınmışsa:
        if (lastClosure != null)
        {
            var salesService = new SalesService(_dbService);
            var localSales = await salesService.GetSalesForDateAsync(targetDate);
            // Kapanış saatinden sonra yapılan yeni satışlar var mı?
            var pendingSales = localSales.Where(s => s.SaleDate > lastClosure.ClosedAt).ToList();

            if (pendingSales.Count > 0)
            {
                return new DailyClosure
                {
                    ClosureDate = targetDate.Date,
                    SalesCount = pendingSales.Count,
                    TotalRevenue = pendingSales.Sum(s => s.GrandTotal),
                    TotalCash = pendingSales.Where(s => s.PaymentType == "Nakit").Sum(s => s.GrandTotal),
                    TotalCreditCard = pendingSales.Where(s => s.PaymentType == "Kredi Kartı" || s.PaymentType == "Kart").Sum(s => s.GrandTotal),
                    TotalOnAccount = pendingSales.Where(s => s.PaymentType == "Veresiye").Sum(s => s.GrandTotal),
                    TotalCost = pendingSales.Sum(s => s.CostTotal),
                    TotalProfit = pendingSales.Sum(s => s.GrandTotal) - pendingSales.Sum(s => s.CostTotal),
                    Notes = $"Son Kapanış Sonrası Satışlar ({lastClosure.ClosedAt:HH:mm})"
                };
            }

            // Kapanış yapılmış ve sonrasında yeni satış yok -> Sıfırla
            return new DailyClosure
            {
                ClosureDate = targetDate.Date,
                SalesCount = 0,
                TotalRevenue = 0m,
                TotalCash = 0m,
                TotalCreditCard = 0m,
                TotalOnAccount = 0m,
                TotalCost = 0m,
                TotalProfit = 0m,
                Notes = $"Gün Sonu Alındı ({lastClosure.ClosedAt:HH:mm}) - Dashboard Sıfırlandı"
            };
        }

        // Henüz gün sonu alınmamışsa günün normal hesaplamasını getir
        return await CalculateDailySummaryAsync(targetDate);
    }

    public async Task<DailyClosure> CalculateDailySummaryAsync(DateTime targetDate)
    {
        var summary = new DailyClosure
        {
            ClosureDate = targetDate.Date,
            ClosedAt = DateTime.Now
        };

        // Eğer daha önce kapanış alınmışsa, sadece o kapanıştan sonraki satışları hesapla
        var closures = await GetClosuresAsync();
        var lastClosure = closures
            .Where(c => c.ClosureDate.Date == targetDate.Date)
            .OrderByDescending(c => c.ClosedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefault();
        DateTime? sinceTime = lastClosure?.ClosedAt;

        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var connection = new SqlConnection(_dbService.ConnectionString);
                await connection.OpenAsync();

                var salesQuery = @"
                    SELECT 
                        COUNT(*) AS SalesCount,
                        ISNULL(SUM(GrandTotal), 0) AS TotalRevenue,
                        ISNULL(SUM(CASE WHEN PaymentType = N'Nakit' THEN GrandTotal ELSE 0 END), 0) AS TotalCash,
                        ISNULL(SUM(CASE WHEN PaymentType = N'Kredi Kartı' OR PaymentType = N'Kart' THEN GrandTotal ELSE 0 END), 0) AS TotalCreditCard,
                        ISNULL(SUM(CASE WHEN PaymentType = N'Veresiye' THEN GrandTotal ELSE 0 END), 0) AS TotalOnAccount,
                        ISNULL(SUM(CostTotal), 0) AS TotalCost
                    FROM Sales 
                    WHERE CAST(SaleDate AS DATE) = @TargetDate" +
                    (sinceTime.HasValue ? " AND SaleDate > @SinceTime" : "");

                await using (var cmd = new SqlCommand(salesQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@TargetDate", targetDate.Date);
                    if (sinceTime.HasValue) cmd.Parameters.AddWithValue("@SinceTime", sinceTime.Value);

                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        summary.SalesCount = reader.GetInt32(0);
                        summary.TotalRevenue = reader.GetDecimal(1);
                        summary.TotalCash = reader.GetDecimal(2);
                        summary.TotalCreditCard = reader.GetDecimal(3);
                        summary.TotalOnAccount = reader.GetDecimal(4);
                        summary.TotalCost = reader.GetDecimal(5);
                        summary.TotalProfit = summary.TotalRevenue - summary.TotalCost;
                    }
                }

                var expenseQuery = @"
                    SELECT ISNULL(SUM(Amount), 0) 
                    FROM Expenses 
                    WHERE CAST(ExpenseDate AS DATE) = @TargetDate" +
                    (sinceTime.HasValue ? " AND ExpenseDate > @SinceTime" : "");

                await using (var cmdExp = new SqlCommand(expenseQuery, connection))
                {
                    cmdExp.Parameters.AddWithValue("@TargetDate", targetDate.Date);
                    if (sinceTime.HasValue) cmdExp.Parameters.AddWithValue("@SinceTime", sinceTime.Value);

                    var expResult = await cmdExp.ExecuteScalarAsync();
                    if (expResult != null && expResult != DBNull.Value)
                    {
                        summary.TotalExpenses = Convert.ToDecimal(expResult);
                        summary.TotalProfit -= summary.TotalExpenses;
                    }
                }

                return summary;
            }
            catch { }
        }

        // Yerel satış kayıtlarından hesapla
        var salesService = new SalesService(_dbService);
        var localSales = await salesService.GetSalesForDateAsync(targetDate);
        if (sinceTime.HasValue)
        {
            localSales = localSales.Where(s => s.SaleDate > sinceTime.Value).ToList();
        }

        if (localSales.Count > 0)
        {
            summary.SalesCount = localSales.Count;
            summary.TotalRevenue = localSales.Sum(s => s.GrandTotal);
            summary.TotalCash = localSales.Where(s => s.PaymentType == "Nakit").Sum(s => s.GrandTotal);
            summary.TotalCreditCard = localSales.Where(s => s.PaymentType == "Kredi Kartı" || s.PaymentType == "Kart").Sum(s => s.GrandTotal);
            summary.TotalOnAccount = localSales.Where(s => s.PaymentType == "Veresiye").Sum(s => s.GrandTotal);
            summary.TotalCost = localSales.Sum(s => s.CostTotal);
            summary.TotalProfit = summary.TotalRevenue - summary.TotalCost;
            summary.Notes = sinceTime.HasValue 
                ? $"Son Kapanış Sonrası Satışlar ({localSales.Count} Fiş)" 
                : $"Günlük Satışlardan Hesaplandı ({localSales.Count} Fiş)";
            return summary;
        }

        summary.SalesCount = 0;
        summary.TotalRevenue = 0m;
        summary.TotalCash = 0m;
        summary.TotalCreditCard = 0m;
        summary.TotalOnAccount = 0m;
        summary.TotalCost = 0m;
        summary.TotalExpenses = 0m;
        summary.TotalProfit = 0m;
        summary.Notes = sinceTime.HasValue ? "Son kapanıştan sonra yeni satış yok" : "O güne ait henüz satış kaydı yok";

        return summary;
    }

    public async Task<bool> SaveClosureAsync(DailyClosure closure)
    {
        closure.ClosedAt = DateTime.Now;

        // 1. Yerel JSON dosyasına kaydet
        await SaveToLocalJsonAsync(closure);

        // 2. SQL Server bağlıysa veritabanına ekle
        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var connection = new SqlConnection(_dbService.ConnectionString);
                await connection.OpenAsync();

                var insertSql = @"
                    INSERT INTO Closures (ClosureDate, ClosedAt, TotalRevenue, TotalCash, TotalCreditCard, TotalOnAccount, TotalCost, TotalProfit, TotalExpenses, SalesCount, Notes, ZReportCash, ZReportCredit, CashDiff, CreditDiff, CashExpense, CreditExpense, VatAmount, PocketCash, PocketCredit, IbanAmount, ExpectedBankPayment)
                    VALUES (@ClosureDate, @ClosedAt, @TotalRevenue, @TotalCash, @TotalCreditCard, @TotalOnAccount, @TotalCost, @TotalProfit, @TotalExpenses, @SalesCount, @Notes, @ZReportCash, @ZReportCredit, @CashDiff, @CreditDiff, @CashExpense, @CreditExpense, @VatAmount, @PocketCash, @PocketCredit, @IbanAmount, @ExpectedBankPayment);
                    SELECT SCOPE_IDENTITY();";

                await using var cmd = new SqlCommand(insertSql, connection);
                cmd.Parameters.AddWithValue("@ClosureDate", closure.ClosureDate.Date);
                cmd.Parameters.AddWithValue("@ClosedAt", closure.ClosedAt);
                cmd.Parameters.AddWithValue("@TotalRevenue", closure.TotalRevenue);
                cmd.Parameters.AddWithValue("@TotalCash", closure.TotalCash);
                cmd.Parameters.AddWithValue("@TotalCreditCard", closure.TotalCreditCard);
                cmd.Parameters.AddWithValue("@TotalOnAccount", closure.TotalOnAccount);
                cmd.Parameters.AddWithValue("@TotalCost", closure.TotalCost);
                cmd.Parameters.AddWithValue("@TotalProfit", closure.TotalProfit);
                cmd.Parameters.AddWithValue("@TotalExpenses", closure.TotalExpenses);
                cmd.Parameters.AddWithValue("@SalesCount", closure.SalesCount);
                cmd.Parameters.AddWithValue("@Notes", (object?)closure.Notes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ZReportCash", closure.ZReportCash);
                cmd.Parameters.AddWithValue("@ZReportCredit", closure.ZReportCredit);
                cmd.Parameters.AddWithValue("@CashDiff", closure.CashDiff);
                cmd.Parameters.AddWithValue("@CreditDiff", closure.CreditDiff);
                cmd.Parameters.AddWithValue("@CashExpense", closure.CashExpense);
                cmd.Parameters.AddWithValue("@CreditExpense", closure.CreditExpense);
                cmd.Parameters.AddWithValue("@VatAmount", closure.VatAmount);
                cmd.Parameters.AddWithValue("@PocketCash", closure.PocketCash);
                cmd.Parameters.AddWithValue("@PocketCredit", closure.PocketCredit);
                cmd.Parameters.AddWithValue("@IbanAmount", closure.IbanAmount);
                cmd.Parameters.AddWithValue("@ExpectedBankPayment", closure.ExpectedBankPayment);

                var newId = await cmd.ExecuteScalarAsync();
                if (newId != null && newId != DBNull.Value)
                {
                    closure.Id = Convert.ToInt64(newId);
                }
            }
            catch { }
        }

        // 3. Kasa oturumunu kapat (Gün bitişi)
        var cashService = new CashService();
        await cashService.CloseTodaySessionAsync(closure.ClosureDate, closure.ClosedAt);

        // 4. Gün sonu otomatik veri gönderimi (Değişen verileri paketle ve merkeze ilet)
        try
        {
            var settings = ThemeService.Current;
            if (settings == null || settings.AutoSyncOnClosure)
            {
                var syncService = new BranchSyncService(_dbService);
                await syncService.AutoDispatchClosureDataAsync(closure.ClosureDate, closure);
            }
        }
        catch { }

        return true;
    }

    public async Task<List<DailyClosure>> GetClosuresAsync()
    {
        var closures = new List<DailyClosure>();

        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var connection = new SqlConnection(_dbService.ConnectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        Id, ClosureDate, ClosedAt, TotalRevenue, TotalCash, TotalCreditCard, 
                        TotalOnAccount, TotalCost, TotalProfit, TotalExpenses, SalesCount, Notes,
                        ISNULL(ZReportCash, 0), ISNULL(ZReportCredit, 0), ISNULL(CashDiff, 0), 
                        ISNULL(CreditDiff, 0), ISNULL(CashExpense, 0), ISNULL(CreditExpense, 0), 
                        ISNULL(VatAmount, 0), ISNULL(PocketCash, 0), ISNULL(PocketCredit, 0), 
                        ISNULL(IbanAmount, 0), ISNULL(ExpectedBankPayment, 0), 
                        Source, SourceSheet, SourceRow 
                    FROM Closures 
                    ORDER BY ClosureDate DESC, ClosedAt DESC, Id DESC";
                await using var cmd = new SqlCommand(sql, connection);
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    closures.Add(new DailyClosure
                    {
                        Id = reader.GetInt64(0),
                        ClosureDate = reader.GetDateTime(1),
                        ClosedAt = reader.GetDateTime(2),
                        TotalRevenue = reader.GetDecimal(3),
                        TotalCash = reader.GetDecimal(4),
                        TotalCreditCard = reader.GetDecimal(5),
                        TotalOnAccount = reader.GetDecimal(6),
                        TotalCost = reader.GetDecimal(7),
                        TotalProfit = reader.GetDecimal(8),
                        TotalExpenses = reader.GetDecimal(9),
                        SalesCount = reader.GetInt32(10),
                        Notes = reader.IsDBNull(11) ? null : reader.GetString(11),
                        ZReportCash = reader.GetDecimal(12),
                        ZReportCredit = reader.GetDecimal(13),
                        CashDiff = reader.GetDecimal(14),
                        CreditDiff = reader.GetDecimal(15),
                        CashExpense = reader.GetDecimal(16),
                        CreditExpense = reader.GetDecimal(17),
                        VatAmount = reader.GetDecimal(18),
                        PocketCash = reader.GetDecimal(19),
                        PocketCredit = reader.GetDecimal(20),
                        IbanAmount = reader.GetDecimal(21),
                        ExpectedBankPayment = reader.GetDecimal(22),
                        Source = reader.IsDBNull(23) ? null : reader.GetString(23),
                        SourceSheet = reader.IsDBNull(24) ? null : reader.GetString(24),
                        SourceRow = reader.IsDBNull(25) ? null : reader.GetInt32(25)
                    });
                }

                if (closures.Count > 0)
                {
                    return closures;
                }
            }
            catch { }
        }

        return await LoadFromLocalJsonAsync();
    }

    private static async Task SaveToLocalJsonAsync(DailyClosure closure)
    {
        try
        {
            EnsureStorageDirectory();
            var list = await LoadFromLocalJsonAsync();
            if (closure.Id == 0)
            {
                closure.Id = list.Count > 0 ? list.Max(x => x.Id) + 1 : 1;
            }
            list.Insert(0, closure);

            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(StorageFile, json);
        }
        catch { }
    }

    private static async Task<List<DailyClosure>> LoadFromLocalJsonAsync()
    {
        try
        {
            if (File.Exists(StorageFile))
            {
                var json = await File.ReadAllTextAsync(StorageFile);
                var list = JsonSerializer.Deserialize<List<DailyClosure>>(json);
                if (list != null) return list;
            }
        }
        catch { }

        return new List<DailyClosure>();
    }

    /// <summary>
    /// Geçmiş bir gün sonu (Z Raporu) kaydını siler.
    /// </summary>
    public async Task<bool> DeleteClosureAsync(long closureId)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                try
                {
                    await using var conn = new SqlConnection(_dbService.ConnectionString);
                    await conn.OpenAsync();
                    var sql = "DELETE FROM Closures WHERE Id = @Id";
                    await using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Id", closureId);
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClosureService] SQL Gün sonu silme hatası: {ex.Message}");
                }
            }

            // Yerel JSON'dan da sil
            try
            {
                if (File.Exists(StorageFile))
                {
                    var list = await LoadFromLocalJsonAsync();
                    list.RemoveAll(c => c.Id == closureId);
                    var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(StorageFile, json);
                }
            }
            catch { }

            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Gün sonu kaydı silinirken hata: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Geçmiş bir gün sonu notunu veya detayını günceller.
    /// </summary>
    public async Task<bool> UpdateClosureNotesAsync(long closureId, string notes)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                try
                {
                    await using var conn = new SqlConnection(_dbService.ConnectionString);
                    await conn.OpenAsync();
                    var sql = "UPDATE Closures SET Notes = @Notes WHERE Id = @Id";
                    await using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Id", closureId);
                    await cmd.ExecuteNonQueryAsync();
                }
                catch { }
            }

            try
            {
                if (File.Exists(StorageFile))
                {
                    var list = await LoadFromLocalJsonAsync();
                    var item = list.FirstOrDefault(c => c.Id == closureId);
                    if (item != null)
                    {
                        item.Notes = notes;
                        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(StorageFile, json);
                    }
                }
            }
            catch { }

            return true;
        }
        catch { return false; }
    }
}
