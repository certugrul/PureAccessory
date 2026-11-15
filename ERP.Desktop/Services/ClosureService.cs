using ERP.Desktop.Models;

namespace ERP.Desktop.Services;

public class ClosureService
{
    private readonly DatabaseService _dbService;

    public ClosureService(DatabaseService? dbService = null)
    {
        _dbService = dbService ?? new DatabaseService();
    }

    private static DailyClosure MapClosureReader(System.Data.Common.DbDataReader reader)
    {
        return new DailyClosure
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
        };
    }

    /// <summary>
    /// Belirli bir tarihe ait en son alınan gün sonu (Z raporu) kaydını tekil ve hızlı sorguyla getirir.
    /// Tüm geçmiş yılları (2025 vb.) taramaz.
    /// </summary>
    public async Task<DailyClosure?> GetLastClosureForDateAsync(DateTime targetDate)
    {
        var start = targetDate.Date;
        var end = start.AddDays(1);

        if (await _dbService.TestConnectionAsync())
        {
            try
            {
                await using var connection = _dbService.CreateConnection();
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        Id, ClosureDate, ClosedAt, TotalRevenue, TotalCash, TotalCreditCard, 
                        TotalOnAccount, TotalCost, TotalProfit, TotalExpenses, SalesCount, Notes,
                        COALESCE(ZReportCash, 0), COALESCE(ZReportCredit, 0), COALESCE(CashDiff, 0), 
                        COALESCE(CreditDiff, 0), COALESCE(CashExpense, 0), COALESCE(CreditExpense, 0), 
                        COALESCE(VatAmount, 0), COALESCE(PocketCash, 0), COALESCE(PocketCredit, 0), 
                        COALESCE(IbanAmount, 0), COALESCE(ExpectedBankPayment, 0), 
                        Source, SourceSheet, SourceRow 
                    FROM Closures 
                    WHERE ClosureDate >= @StartDate AND ClosureDate < @EndDate
                    ORDER BY ClosedAt DESC, Id DESC
                    LIMIT 1";
                await using var cmd = connection.CreateCommand(sql);
                cmd.AddParam("StartDate", start);
                cmd.AddParam("EndDate", end);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapClosureReader(reader);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClosureService] GetLastClosureForDateAsync hatası: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Belirli bir tarihe ait tüm gün sonu kayıtlarını getirir.
    /// </summary>
    public async Task<List<DailyClosure>> GetClosuresForDateAsync(DateTime targetDate)
    {
        return await GetClosuresAsync(exactDate: targetDate.Date);
    }

    /// <summary>
    /// Dashboard ekranında gösterilecek aktif (kapanıştan sonraki) günlük metrikleri getirir.
    /// Gün sonu alınmışsa ve sonrasında yeni satış yapılmamışsa 0 döner.
    /// </summary>
    public async Task<DailyClosure> GetActiveDashboardMetricsAsync(DateTime targetDate)
    {
        var lastClosure = await GetLastClosureForDateAsync(targetDate);

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
                    TotalCash = pendingSales.Sum(s => s.CashAmount > 0 ? s.CashAmount : (s.PaymentType == "Nakit" ? s.GrandTotal : 0m)),
                    TotalCreditCard = pendingSales.Sum(s => s.CardAmount > 0 ? s.CardAmount : (s.PaymentType == "Kredi Kartı" || s.PaymentType == "Kart" ? s.GrandTotal : 0m)),
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
        var startDate = targetDate.Date;
        var endDate = startDate.AddDays(1);

        var summary = new DailyClosure
        {
            ClosureDate = startDate,
            ClosedAt = DateTime.Now
        };

        DateTime? sinceTime = null;

        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var connection = _dbService.CreateConnection();
                await connection.OpenAsync();

                // 1. Aynı bağlantı üzerinden o günün son kapanış saatini kontrol et
                var lastClosureSql = @"
                    SELECT ClosedAt 
                    FROM Closures 
                    WHERE ClosureDate >= @StartDate AND ClosureDate < @EndDate
                    ORDER BY ClosedAt DESC, Id DESC 
                    LIMIT 1";
                await using (var cmdLast = connection.CreateCommand(lastClosureSql))
                {
                    cmdLast.AddParam("StartDate", startDate);
                    cmdLast.AddParam("EndDate", endDate);
                    var lastObj = await cmdLast.ExecuteScalarAsync();
                    if (lastObj != null && lastObj != DBNull.Value)
                    {
                        sinceTime = Convert.ToDateTime(lastObj);
                    }
                }

                // 2. Sadece o günün tarih aralığındaki (index dostu) satışları topla
                var salesQuery = @"
                    SELECT 
                        COUNT(*) AS SalesCount,
                        COALESCE(SUM(GrandTotal), 0) AS TotalRevenue,
                        COALESCE(SUM(CASE WHEN CashAmount > 0 THEN CashAmount WHEN PaymentType = 'Nakit' THEN GrandTotal ELSE 0 END), 0) AS TotalCash,
                        COALESCE(SUM(CASE WHEN CardAmount > 0 THEN CardAmount WHEN PaymentType = 'Kredi Kartı' OR PaymentType = 'Kart' THEN GrandTotal ELSE 0 END), 0) AS TotalCreditCard,
                        COALESCE(SUM(CASE WHEN PaymentType = 'Veresiye' THEN GrandTotal ELSE 0 END), 0) AS TotalOnAccount,
                        COALESCE(SUM(CostTotal), 0) AS TotalCost
                    FROM Sales 
                    WHERE SaleDate >= @StartDate AND SaleDate < @EndDate" +
                    (sinceTime.HasValue ? " AND SaleDate > @SinceTime" : "");

                await using (var cmd = connection.CreateCommand(salesQuery))
                {
                    cmd.AddParam("StartDate", startDate);
                    cmd.AddParam("EndDate", endDate);
                    if (sinceTime.HasValue) cmd.AddParam("SinceTime", sinceTime.Value);

                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        summary.SalesCount = Convert.ToInt32(reader.GetValue(0));
                        summary.TotalRevenue = Convert.ToDecimal(reader.GetValue(1));
                        summary.TotalCash = Convert.ToDecimal(reader.GetValue(2));
                        summary.TotalCreditCard = Convert.ToDecimal(reader.GetValue(3));
                        summary.TotalOnAccount = Convert.ToDecimal(reader.GetValue(4));
                        summary.TotalCost = Convert.ToDecimal(reader.GetValue(5));
                        summary.TotalProfit = summary.TotalRevenue - summary.TotalCost;
                    }
                }

                // 3. Sadece o günün tarih aralığındaki giderleri topla
                var expenseQuery = @"
                    SELECT COALESCE(SUM(Amount), 0) 
                    FROM Expenses 
                    WHERE ExpenseDate >= @StartDate AND ExpenseDate < @EndDate" +
                    (sinceTime.HasValue ? " AND ExpenseDate > @SinceTime" : "");

                await using (var cmdExp = connection.CreateCommand(expenseQuery))
                {
                    cmdExp.AddParam("StartDate", startDate);
                    cmdExp.AddParam("EndDate", endDate);
                    if (sinceTime.HasValue) cmdExp.AddParam("SinceTime", sinceTime.Value);

                    var expResult = await cmdExp.ExecuteScalarAsync();
                    if (expResult != null && expResult != DBNull.Value)
                    {
                        summary.TotalExpenses = Convert.ToDecimal(expResult);
                        summary.TotalProfit -= summary.TotalExpenses;
                    }
                }

                return summary;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClosureService] CalculateDailySummaryAsync hatası: {ex.Message}");
            }
        }

        // Yedek hesaplama
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
            summary.TotalCash = localSales.Sum(s => s.CashAmount > 0 ? s.CashAmount : (s.PaymentType == "Nakit" ? s.GrandTotal : 0m));
            summary.TotalCreditCard = localSales.Sum(s => s.CardAmount > 0 ? s.CardAmount : (s.PaymentType == "Kredi Kartı" || s.PaymentType == "Kart" ? s.GrandTotal : 0m));
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

        // 1. Veritabanı bağlıysa veritabanına ekle ve aynı bağlantıda kasayı kapat
        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var connection = _dbService.CreateConnection();
                await connection.OpenAsync();

                var insertSql = @"
                    INSERT INTO Closures (ClosureDate, ClosedAt, TotalRevenue, TotalCash, TotalCreditCard, TotalOnAccount, TotalCost, TotalProfit, TotalExpenses, SalesCount, Notes, ZReportCash, ZReportCredit, CashDiff, CreditDiff, CashExpense, CreditExpense, VatAmount, PocketCash, PocketCredit, IbanAmount, ExpectedBankPayment)
                    VALUES (@ClosureDate, @ClosedAt, @TotalRevenue, @TotalCash, @TotalCreditCard, @TotalOnAccount, @TotalCost, @TotalProfit, @TotalExpenses, @SalesCount, @Notes, @ZReportCash, @ZReportCredit, @CashDiff, @CreditDiff, @CashExpense, @CreditExpense, @VatAmount, @PocketCash, @PocketCredit, @IbanAmount, @ExpectedBankPayment);
                    SELECT SCOPE_IDENTITY();";

                await using var cmd = connection.CreateCommand(insertSql);
                cmd.AddParam("ClosureDate", closure.ClosureDate.Date);
                cmd.AddParam("ClosedAt", closure.ClosedAt);
                cmd.AddParam("TotalRevenue", closure.TotalRevenue);
                cmd.AddParam("TotalCash", closure.TotalCash);
                cmd.AddParam("TotalCreditCard", closure.TotalCreditCard);
                cmd.AddParam("TotalOnAccount", closure.TotalOnAccount);
                cmd.AddParam("TotalCost", closure.TotalCost);
                cmd.AddParam("TotalProfit", closure.TotalProfit);
                cmd.AddParam("TotalExpenses", closure.TotalExpenses);
                cmd.AddParam("SalesCount", closure.SalesCount);
                cmd.AddParam("Notes", closure.Notes);
                cmd.AddParam("ZReportCash", closure.ZReportCash);
                cmd.AddParam("ZReportCredit", closure.ZReportCredit);
                cmd.AddParam("CashDiff", closure.CashDiff);
                cmd.AddParam("CreditDiff", closure.CreditDiff);
                cmd.AddParam("CashExpense", closure.CashExpense);
                cmd.AddParam("CreditExpense", closure.CreditExpense);
                cmd.AddParam("VatAmount", closure.VatAmount);
                cmd.AddParam("PocketCash", closure.PocketCash);
                cmd.AddParam("PocketCredit", closure.PocketCredit);
                cmd.AddParam("IbanAmount", closure.IbanAmount);
                cmd.AddParam("ExpectedBankPayment", closure.ExpectedBankPayment);

                var newId = await cmd.ExecuteScalarAsync();
                if (newId != null && newId != DBNull.Value)
                {
                    closure.Id = Convert.ToInt64(newId);
                }

                // 2. Kasa oturumunu aynı açık bağlantı üzerinden tek sorguyla anında kapat!
                var closeSessionSql = @"UPDATE CashSessions
                                        SET Status = 'Kapalı', ClosedAt = @closed
                                        WHERE CAST(SessionDate AS DATE) <= @date AND Status != 'Kapalı'";
                await using var closeCmd = connection.CreateCommand(closeSessionSql);
                closeCmd.AddParam("closed", closure.ClosedAt);
                closeCmd.AddParam("date", closure.ClosureDate.Date);
                await closeCmd.ExecuteNonQueryAsync();

                AppDataCache.UpdateTodayCashSession(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClosureService] SaveClosureAsync hatası: {ex.Message}");
            }
        }
        else
        {
            var cashService = new CashService(_dbService);
            await cashService.CloseTodaySessionAsync(closure.ClosureDate, closure.ClosedAt);
        }

        // 3. Ekranları gün sonu hakkında haberdar et
        FinancialSyncBus.Notify(FinancialChangeType.DayClosure, closure);

        // 4. Otomatik Gün Sonu Z Raporu PDF ve E-Posta Gönderimi (Arka planda, UI'ı hiç bekletmeden)
        try
        {
            var settings = ThemeService.Current;
            if (settings != null && settings.AutoEmailZReportOnClosure)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var emailService = new ZReportEmailService(_dbService);
                        await emailService.SendZReportEmailAsync(closure.Id, closure.ClosureDate);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ClosureService] Z Raporu e-posta gönderim hatası: {ex.Message}");
                    }
                });
            }
        }
        catch { }

        return true;
    }

    /// <summary>
    /// Gün sonu kayıtlarını getirir.
    /// Tarih veya dönem seçilmediğinde varsayılan olarak sadece içinde bulunulan yılın (örn. 2026) kayıtlarını yükler;
    /// 2025 gibi geçmiş yılların verilerini boşuna indirmez.
    /// Kullanıcı geçmiş bir tarih (örn. 2025) veya tarih aralığı seçtiğinde o tarihin verilerini doğrudan veritabanından getirir.
    /// </summary>
    public async Task<List<DailyClosure>> GetClosuresAsync(
        DateTime? exactDate = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        bool includeAllHistory = false)
    {
        var closures = new List<DailyClosure>();

        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var connection = _dbService.CreateConnection();
                await connection.OpenAsync();

                string whereClause = "";
                DateTime? filterStart = null;
                DateTime? filterEnd = null;

                if (exactDate.HasValue)
                {
                    filterStart = exactDate.Value.Date;
                    filterEnd = filterStart.Value.AddDays(1);
                    whereClause = "WHERE ClosureDate >= @StartDate AND ClosureDate < @EndDate";
                }
                else if (startDate.HasValue && endDate.HasValue)
                {
                    filterStart = startDate.Value.Date;
                    filterEnd = endDate.Value.Date.AddDays(1);
                    whereClause = "WHERE ClosureDate >= @StartDate AND ClosureDate < @EndDate";
                }
                else if (startDate.HasValue)
                {
                    filterStart = startDate.Value.Date;
                    whereClause = "WHERE ClosureDate >= @StartDate";
                }
                else if (!includeAllHistory)
                {
                    // Tarih seçilmediğinde eski yılları (2025 vb.) boşuna yükleme, sadece aktif yılı yükle
                    filterStart = new DateTime(DateTime.Today.Year, 1, 1);
                    whereClause = "WHERE ClosureDate >= @StartDate";
                }

                var sql = $@"
                    SELECT 
                        Id, ClosureDate, ClosedAt, TotalRevenue, TotalCash, TotalCreditCard, 
                        TotalOnAccount, TotalCost, TotalProfit, TotalExpenses, SalesCount, Notes,
                        COALESCE(ZReportCash, 0), COALESCE(ZReportCredit, 0), COALESCE(CashDiff, 0), 
                        COALESCE(CreditDiff, 0), COALESCE(CashExpense, 0), COALESCE(CreditExpense, 0), 
                        COALESCE(VatAmount, 0), COALESCE(PocketCash, 0), COALESCE(PocketCredit, 0), 
                        COALESCE(IbanAmount, 0), COALESCE(ExpectedBankPayment, 0), 
                        Source, SourceSheet, SourceRow 
                    FROM Closures 
                    {whereClause}
                    ORDER BY ClosureDate DESC, ClosedAt DESC, Id DESC";

                await using var cmd = connection.CreateCommand(sql);
                if (filterStart.HasValue) cmd.AddParam("StartDate", filterStart.Value);
                if (filterEnd.HasValue) cmd.AddParam("EndDate", filterEnd.Value);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    closures.Add(MapClosureReader(reader));
                }

                return closures;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClosureService] GetClosuresAsync hatası: {ex.Message}");
            }
        }

        return closures;
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
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();
                    var sql = "DELETE FROM Closures WHERE Id = @Id";
                    await using var cmd = conn.CreateCommand(sql);
                    cmd.AddParam("Id", closureId);
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClosureService] DB Gün sonu silme hatası: {ex.Message}");
                }
            }

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
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();
                    var sql = "UPDATE Closures SET Notes = @Notes WHERE Id = @Id";
                    await using var cmd = conn.CreateCommand(sql);
                    cmd.AddParam("Notes", notes);
                    cmd.AddParam("Id", closureId);
                    await cmd.ExecuteNonQueryAsync();
                }
                catch { }
            }

            return true;
        }
        catch { return false; }
    }
}
