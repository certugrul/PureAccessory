using System.Data.Common;
using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;

namespace ERP.Desktop.Services;

public class DebtService
{
    private readonly DatabaseService _dbService;
    private readonly InterestEngine _interestEngine;
    private readonly FinancialAuditService _auditService;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string DebtsJsonFile = Path.Combine(StorageDirectory, "financial_debts.json");

    public DebtService(
        DatabaseService? dbService = null,
        InterestEngine? interestEngine = null,
        FinancialAuditService? auditService = null)
    {
        _dbService = dbService ?? new DatabaseService();
        _interestEngine = interestEngine ?? new InterestEngine(_dbService);
        _auditService = auditService ?? new FinancialAuditService(_dbService);
    }

    public async Task<List<FinancialDebt>> GetAllDebtsAsync(bool includeInactive = false)
    {
        var list = new List<FinancialDebt>();
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                var sql = @"
                    SELECT d.Id, d.Title, ISNULL(d.Creditor, ISNULL(d.Lender, '')) AS Creditor, 
                           ISNULL(d.DebtType, 'Kredi / Borç') AS DebtType, d.BankAccountId, d.TotalAmount, d.RemainingAmount, 
                           d.PrincipalAmount, d.AccruedInterest, d.AccruedTax, d.MinPaymentAmount, d.InterestRate, 
                           d.LateInterestRate, ISNULL(d.InstallmentAmount, ISNULL(d.MonthlyInstallment, 0)) AS InstallmentAmount, 
                           ISNULL(d.TotalInstallments, 1) AS TotalInstallments, ISNULL(d.RemainingInstallments, 1) AS RemainingInstallments, 
                           ISNULL(d.DayOfMonth, 15) AS DayOfMonth, d.StartDate, d.LastInterestCalculationDate, d.CutoffDate, 
                           ISNULL(d.Status, 'Devam Ediyor') AS Status, d.Notes, d.IsActive,
                           b.AccountName AS BankAccountName
                    FROM FinancialDebts d
                    LEFT JOIN BankAccounts b ON d.BankAccountId = b.Id
                    WHERE (@all = 1 OR d.IsActive = 1)
                    ORDER BY d.Status ASC, d.DayOfMonth ASC";

                await using var cmd = conn.CreateCommand(sql);
                cmd.AddParam("all", includeInactive ? 1 : 0);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new FinancialDebt
                    {
                        Id = Convert.ToInt32(reader[0]),
                        Title = reader.GetString(1),
                        Creditor = reader.GetString(2),
                        DebtType = reader.GetString(3),
                        BankAccountId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        TotalAmount = reader.GetDecimal(5),
                        RemainingAmount = reader.GetDecimal(6),
                        PrincipalAmount = reader.GetDecimal(7),
                        AccruedInterest = reader.GetDecimal(8),
                        AccruedTax = reader.GetDecimal(9),
                        MinPaymentAmount = reader.GetDecimal(10),
                        InterestRate = reader.GetDecimal(11),
                        LateInterestRate = reader.GetDecimal(12),
                        InstallmentAmount = reader.GetDecimal(13),
                        TotalInstallments = Convert.ToInt32(reader[14]),
                        RemainingInstallments = Convert.ToInt32(reader[15]),
                        DayOfMonth = Convert.ToInt32(reader[16]),
                        StartDate = reader.GetDateTime(17),
                        LastInterestCalculationDate = reader.IsDBNull(18) ? null : reader.GetDateTime(18),
                        CutoffDate = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
                        Status = reader.GetString(20),
                        Notes = reader.IsDBNull(21) ? null : reader.GetString(21),
                        IsActive = reader.GetBoolean(22),
                        BankAccountName = reader.IsDBNull(23) ? null : reader.GetString(23)
                    });
                }
                if (list.Count > 0) return list;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DebtService] GetAllDebtsAsync DB Hatası: {ex.Message}");
        }

        // Yerel JSON dosyasından geri dönüş
        try
        {
            if (File.Exists(DebtsJsonFile))
            {
                var json = await File.ReadAllTextAsync(DebtsJsonFile);
                var jsonList = JsonSerializer.Deserialize<List<FinancialDebt>>(json);
                if (jsonList != null)
                {
                    return jsonList.Where(d => includeInactive || d.IsActive).ToList();
                }
            }
        }
        catch { }

        return list;
    }

    public async Task<FinancialDebt?> GetDebtByIdAsync(int id)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                var sql = @"
                    SELECT d.Id, d.Title, ISNULL(d.Creditor, ISNULL(d.Lender, '')) AS Creditor, 
                           ISNULL(d.DebtType, 'Kredi / Borç') AS DebtType, d.BankAccountId, d.TotalAmount, d.RemainingAmount, 
                           d.PrincipalAmount, d.AccruedInterest, d.AccruedTax, d.MinPaymentAmount, d.InterestRate, 
                           d.LateInterestRate, ISNULL(d.InstallmentAmount, ISNULL(d.MonthlyInstallment, 0)) AS InstallmentAmount, 
                           ISNULL(d.TotalInstallments, 1) AS TotalInstallments, ISNULL(d.RemainingInstallments, 1) AS RemainingInstallments, 
                           ISNULL(d.DayOfMonth, 15) AS DayOfMonth, d.StartDate, d.LastInterestCalculationDate, d.CutoffDate, 
                           ISNULL(d.Status, 'Devam Ediyor') AS Status, d.Notes, d.IsActive,
                           b.AccountName AS BankAccountName
                    FROM FinancialDebts d
                    LEFT JOIN BankAccounts b ON d.BankAccountId = b.Id
                    WHERE d.Id = @id";

                await using var cmd = conn.CreateCommand(sql);
                cmd.AddParam("id", id);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new FinancialDebt
                    {
                        Id = Convert.ToInt32(reader[0]),
                        Title = reader.GetString(1),
                        Creditor = reader.GetString(2),
                        DebtType = reader.GetString(3),
                        BankAccountId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        TotalAmount = reader.GetDecimal(5),
                        RemainingAmount = reader.GetDecimal(6),
                        PrincipalAmount = reader.GetDecimal(7),
                        AccruedInterest = reader.GetDecimal(8),
                        AccruedTax = reader.GetDecimal(9),
                        MinPaymentAmount = reader.GetDecimal(10),
                        InterestRate = reader.GetDecimal(11),
                        LateInterestRate = reader.GetDecimal(12),
                        InstallmentAmount = reader.GetDecimal(13),
                        TotalInstallments = Convert.ToInt32(reader[14]),
                        RemainingInstallments = Convert.ToInt32(reader[15]),
                        DayOfMonth = Convert.ToInt32(reader[16]),
                        StartDate = reader.GetDateTime(17),
                        LastInterestCalculationDate = reader.IsDBNull(18) ? null : reader.GetDateTime(18),
                        CutoffDate = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
                        Status = reader.GetString(20),
                        Notes = reader.IsDBNull(21) ? null : reader.GetString(21),
                        IsActive = reader.GetBoolean(22),
                        BankAccountName = reader.IsDBNull(23) ? null : reader.GetString(23)
                    };
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DebtService] GetDebtByIdAsync DB Hatası: {ex.Message}");
        }

        try
        {
            if (File.Exists(DebtsJsonFile))
            {
                var json = await File.ReadAllTextAsync(DebtsJsonFile);
                var jsonList = JsonSerializer.Deserialize<List<FinancialDebt>>(json);
                return jsonList?.FirstOrDefault(d => d.Id == id);
            }
        }
        catch { }

        return null;
    }

    public async Task<FinancialDebt> SaveOrUpdateDebtAsync(FinancialDebt debt, string userName = "Yönetici")
    {
        await using var conn = _dbService.CreateConnection();
        await conn.OpenAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            // Saf anapara kontrolü
            if (debt.PrincipalAmount <= 0 && debt.RemainingAmount > 0)
            {
                debt.PrincipalAmount = debt.RemainingAmount;
            }
            if (debt.RemainingAmount <= 0 && debt.PrincipalAmount > 0)
            {
                debt.RemainingAmount = debt.PrincipalAmount + debt.AccruedInterest + debt.AccruedTax;
            }

            if (debt.Id == 0)
            {
                var sql = @"
                    INSERT INTO FinancialDebts (Title, Creditor, DebtType, BankAccountId, TotalAmount, RemainingAmount, 
                                               PrincipalAmount, AccruedInterest, AccruedTax, MinPaymentAmount, InterestRate, 
                                               LateInterestRate, InstallmentAmount, TotalInstallments, RemainingInstallments, 
                                               DayOfMonth, StartDate, LastInterestCalculationDate, CutoffDate, Status, Notes, IsActive)
                    OUTPUT INSERTED.Id
                    VALUES (@t, @cred, @dtype, @baid, @tot, @rem, @prin, @aint, @atax, @min, @rate, @lrate, @inst, @tinst, @rinst, @dom, @sdate, @calc, @cutoff, @stat, @notes, @act);";

                await using var cmd = conn.CreateCommand(sql, tran);
                cmd.AddParam("t", debt.Title);
                cmd.AddParam("cred", debt.Creditor);
                cmd.AddParam("dtype", debt.DebtType);
                cmd.AddParam("baid", (object?)debt.BankAccountId ?? DBNull.Value);
                cmd.AddParam("tot", debt.TotalAmount);
                cmd.AddParam("rem", debt.RemainingAmount);
                cmd.AddParam("prin", debt.PrincipalAmount);
                cmd.AddParam("aint", debt.AccruedInterest);
                cmd.AddParam("atax", debt.AccruedTax);
                cmd.AddParam("min", debt.MinPaymentAmount);
                cmd.AddParam("rate", debt.InterestRate);
                cmd.AddParam("lrate", debt.LateInterestRate);
                cmd.AddParam("inst", debt.InstallmentAmount);
                cmd.AddParam("tinst", debt.TotalInstallments);
                cmd.AddParam("rinst", debt.RemainingInstallments);
                cmd.AddParam("dom", debt.DayOfMonth);
                cmd.AddParam("sdate", debt.StartDate);
                cmd.AddParam("calc", (object?)debt.LastInterestCalculationDate ?? DBNull.Value);
                cmd.AddParam("cutoff", (object?)debt.CutoffDate ?? DBNull.Value);
                cmd.AddParam("stat", debt.Status);
                cmd.AddParam("notes", (object?)debt.Notes ?? DBNull.Value);
                cmd.AddParam("act", debt.IsActive);

                debt.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                // İlk hareket
                var transSql = @"
                    INSERT INTO DebtTransactions (DebtId, TransactionDate, TransactionType, Amount, PrincipalChange, InterestChange, TaxChange, PreviousBalance, NewBalance, Description, ReferenceType)
                    VALUES (@did, @dt, 'Borç Başlangıcı', @amt, @prin, 0, 0, 0, @amt, @desc, 'INIT');";
                await using var transCmd = conn.CreateCommand(transSql, tran);
                transCmd.AddParam("did", debt.Id);
                transCmd.AddParam("dt", debt.StartDate);
                transCmd.AddParam("amt", debt.TotalAmount);
                transCmd.AddParam("prin", debt.PrincipalAmount);
                transCmd.AddParam("desc", $"Borç / Kredi kaydı açıldı ({debt.Title})");
                await transCmd.ExecuteNonQueryAsync();

                await _auditService.LogAsync("INSERT", "FinancialDebt", debt.Id, $"Yeni borç açıldı: {debt.Title} (₺{debt.TotalAmount:N2})", null, debt.TotalAmount.ToString(), debt.BankAccountId, debt.Id, null, userName, conn, tran);
            }
            else
            {
                var sql = @"
                    UPDATE FinancialDebts 
                    SET Title = @t, Creditor = @cred, DebtType = @dtype, BankAccountId = @baid, TotalAmount = @tot, 
                        RemainingAmount = @rem, PrincipalAmount = @prin, AccruedInterest = @aint, AccruedTax = @atax, 
                        MinPaymentAmount = @min, InterestRate = @rate, LateInterestRate = @lrate, InstallmentAmount = @inst, 
                        TotalInstallments = @tinst, RemainingInstallments = @rinst, DayOfMonth = @dom, StartDate = @sdate, 
                        LastInterestCalculationDate = @calc, CutoffDate = @cutoff, Status = @stat, Notes = @notes, IsActive = @act
                    WHERE Id = @id;";

                await using var cmd = conn.CreateCommand(sql, tran);
                cmd.AddParam("id", debt.Id);
                cmd.AddParam("t", debt.Title);
                cmd.AddParam("cred", debt.Creditor);
                cmd.AddParam("dtype", debt.DebtType);
                cmd.AddParam("baid", (object?)debt.BankAccountId ?? DBNull.Value);
                cmd.AddParam("tot", debt.TotalAmount);
                cmd.AddParam("rem", debt.RemainingAmount);
                cmd.AddParam("prin", debt.PrincipalAmount);
                cmd.AddParam("aint", debt.AccruedInterest);
                cmd.AddParam("atax", debt.AccruedTax);
                cmd.AddParam("min", debt.MinPaymentAmount);
                cmd.AddParam("rate", debt.InterestRate);
                cmd.AddParam("lrate", debt.LateInterestRate);
                cmd.AddParam("inst", debt.InstallmentAmount);
                cmd.AddParam("tinst", debt.TotalInstallments);
                cmd.AddParam("rinst", debt.RemainingInstallments);
                cmd.AddParam("dom", debt.DayOfMonth);
                cmd.AddParam("sdate", debt.StartDate);
                cmd.AddParam("calc", (object?)debt.LastInterestCalculationDate ?? DBNull.Value);
                cmd.AddParam("cutoff", (object?)debt.CutoffDate ?? DBNull.Value);
                cmd.AddParam("stat", debt.Status);
                cmd.AddParam("notes", (object?)debt.Notes ?? DBNull.Value);
                cmd.AddParam("act", debt.IsActive);
                await cmd.ExecuteNonQueryAsync();

                await _auditService.LogAsync("UPDATE", "FinancialDebt", debt.Id, $"Borç güncellendi: {debt.Title}", null, debt.RemainingAmount.ToString(), debt.BankAccountId, debt.Id, null, userName, conn, tran);
            }

            await tran.CommitAsync();
            return debt;
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> DeleteDebtAsync(int id, string userName = "Yönetici")
    {
        await using var conn = _dbService.CreateConnection();
        await conn.OpenAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var sql = "UPDATE FinancialDebts SET IsActive = 0, Status = 'İptal / Silindi' WHERE Id = @id";
            await using var cmd = conn.CreateCommand(sql, tran);
            cmd.AddParam("id", id);
            int rows = await cmd.ExecuteNonQueryAsync();

            await _auditService.LogAsync("DELETE", "FinancialDebt", id, $"Borç silindi / pasife alındı (Id: {id})", null, null, null, id, null, userName, conn, tran);

            await tran.CommitAsync();
            return rows > 0;
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    public async Task<List<DebtTransaction>> GetDebtTransactionsAsync(int debtId)
    {
        var list = new List<DebtTransaction>();
        await using var conn = _dbService.CreateConnection();
        await conn.OpenAsync();

        var sql = @"
            SELECT Id, DebtId, TransactionDate, TransactionType, Amount, PrincipalChange, InterestChange, TaxChange, 
                   PreviousBalance, NewBalance, Description, ReferenceId, ReferenceType, UserId, CreatedAt
            FROM DebtTransactions
            WHERE DebtId = @did
            ORDER BY TransactionDate DESC, Id DESC";

        await using var cmd = conn.CreateCommand(sql);
        cmd.AddParam("did", debtId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new DebtTransaction
            {
                Id = reader.GetInt64(0),
                DebtId = reader.GetInt32(1),
                TransactionDate = reader.GetDateTime(2),
                TransactionType = reader.GetString(3),
                Amount = reader.GetDecimal(4),
                PrincipalChange = reader.GetDecimal(5),
                InterestChange = reader.GetDecimal(6),
                TaxChange = reader.GetDecimal(7),
                PreviousBalance = reader.GetDecimal(8),
                NewBalance = reader.GetDecimal(9),
                Description = reader.IsDBNull(10) ? null : reader.GetString(10),
                ReferenceId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
                ReferenceType = reader.IsDBNull(12) ? null : reader.GetString(12),
                UserId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                CreatedAt = reader.GetDateTime(14)
            });
        }
        return list;
    }

    public async Task<List<InterestAccrual>> GetInterestAccrualsAsync(int debtId)
    {
        var list = new List<InterestAccrual>();
        await using var conn = _dbService.CreateConnection();
        await conn.OpenAsync();

        var sql = @"
            SELECT Id, DebtId, FromDate, ToDate, Days, Rate, PrincipalAmount, CalculatedInterest, BsmvAmount, KkdfAmount, TotalTax, TotalAccrual, IsApplied, CreatedAt
            FROM InterestAccruals
            WHERE DebtId = @did
            ORDER BY ToDate DESC, Id DESC";

        await using var cmd = conn.CreateCommand(sql);
        cmd.AddParam("did", debtId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new InterestAccrual
            {
                Id = reader.GetInt64(0),
                DebtId = reader.GetInt32(1),
                FromDate = reader.GetDateTime(2),
                ToDate = reader.GetDateTime(3),
                Days = reader.GetInt32(4),
                Rate = reader.GetDecimal(5),
                PrincipalAmount = reader.GetDecimal(6),
                CalculatedInterest = reader.GetDecimal(7),
                BsmvAmount = reader.GetDecimal(8),
                KkdfAmount = reader.GetDecimal(9),
                TotalTax = reader.GetDecimal(10),
                TotalAccrual = reader.GetDecimal(11),
                IsApplied = reader.GetBoolean(12),
                CreatedAt = reader.GetDateTime(13)
            });
        }
        return list;
    }

    public async Task<List<Payment>> GetPaymentsAsync(int debtId)
    {
        var list = new List<Payment>();
        await using var conn = _dbService.CreateConnection();
        await conn.OpenAsync();

        var sql = @"
            SELECT Id, DebtId, BankAccountId, PaymentDate, TotalPaid, PaymentType, Notes, CreatedBy, CreatedAt
            FROM Payments
            WHERE DebtId = @did
            ORDER BY PaymentDate DESC, Id DESC";

        await using var cmd = conn.CreateCommand(sql);
        cmd.AddParam("did", debtId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Payment
            {
                Id = reader.GetInt64(0),
                DebtId = reader.GetInt32(1),
                BankAccountId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                PaymentDate = reader.GetDateTime(3),
                TotalPaid = reader.GetDecimal(4),
                PaymentType = reader.GetString(5),
                Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedBy = reader.GetString(7),
                CreatedAt = reader.GetDateTime(8)
            });
        }
        return list;
    }

    public async Task<DebtSummaryDto> GetDebtSummaryAsync()
    {
        var summary = new DebtSummaryDto();
        await using var conn = _dbService.CreateConnection();
        await conn.OpenAsync();

        var sql = @"
            SELECT 
                ISNULL(SUM(PrincipalAmount), 0) AS TotalPrincipal,
                ISNULL(SUM(AccruedInterest), 0) AS TotalAccruedInterest,
                ISNULL(SUM(AccruedTax), 0) AS TotalAccruedTax,
                ISNULL(SUM(CASE WHEN DebtType = 'Kredi Kartı' THEN RemainingAmount ELSE 0 END), 0) AS TotalCreditCard,
                ISNULL(SUM(CASE WHEN DebtType = 'KMH / Ek Hesap' THEN RemainingAmount ELSE 0 END), 0) AS TotalKmh,
                ISNULL(SUM(CASE WHEN DebtType NOT IN ('Kredi Kartı', 'KMH / Ek Hesap') THEN RemainingAmount ELSE 0 END), 0) AS TotalLoan,
                ISNULL(SUM(MinPaymentAmount), 0) AS TotalMinPayment
            FROM FinancialDebts
            WHERE IsActive = 1 AND Status != 'Tamamlandı / Kapandı'";

        await using var cmd = conn.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            summary.TotalPrincipal = reader.GetDecimal(0);
            summary.TotalAccruedInterest = reader.GetDecimal(1);
            summary.TotalAccruedTax = reader.GetDecimal(2);
            summary.TotalCreditCardDebt = reader.GetDecimal(3);
            summary.TotalKmhDebt = reader.GetDecimal(4);
            summary.TotalLoanDebt = reader.GetDecimal(5);
            summary.TotalMinPaymentDue = reader.GetDecimal(6);
        }
        return summary;
    }

    /// <summary>
    /// BORÇ / KREDİ KARTI / KMH ÖDEME MOTORU:
    /// SQL Transaction içinde atomik işlem:
    /// 1. Varsa ödeme gününe kadar faiz tahakkukunu hesaplar
    /// 2. Ödeme öncelik kurallarına göre (Faiz -> Vergi -> Masraf -> Anapara) dağıtır
    /// 3. Payments & PaymentAllocations tablolarına yazar
    /// 4. FinancialDebts tablosunda borcu düşer (kısmi ödemede anapara kapanmaz, bakiye güncellenir)
    /// 5. Ödeme bankadan yapıldıysa BankTransactions 'Çıkış' ve BankAccounts bakiyesi düşer
    /// 6. Kart/KMH limitlerini günceller
    /// 7. Gider faturasını ve ilgili takvim hatırlatmasını günceller
    /// 8. DebtTransactions hareket kaydı ekler
    /// 9. FinancialAuditLogs yazar
    /// </summary>
    private static readonly SemaphoreSlim _paymentLock = new(1, 1);
    private static string? _lastPaymentFingerprint;
    private static DateTime _lastPaymentTime = DateTime.MinValue;
    private static PaymentBreakdown? _lastBreakdown;

    public async Task<PaymentBreakdown> ProcessPaymentAsync(
        int debtId,
        decimal paymentAmount,
        string paymentType, // "Tam", "Asgari", "Kısmi"
        int? fromBankAccountId, // Ödemenin çekildiği banka hesabı (null ise nakit)
        DateTime paymentDate,
        string? notes = null,
        string userName = "Yönetici")
    {
        if (paymentAmount <= 0)
            throw new ArgumentException("Ödeme tutarı 0'dan büyük olmalıdır.");

        var fp = $"{debtId}|{paymentAmount:F2}|{paymentType}|{fromBankAccountId}|{paymentDate:yyyyMMdd}";
        await _paymentLock.WaitAsync();
        try
        {
            if (_lastPaymentFingerprint == fp && 
                (DateTime.Now - _lastPaymentTime).TotalSeconds < 3.0 && 
                _lastBreakdown != null)
            {
                System.Diagnostics.Debug.WriteLine($"[DebtService] Mükerrer borç ödemesi engellendi: {fp}");
                return _lastBreakdown;
            }

            await using var conn = _dbService.CreateConnection();
            await conn.OpenAsync();
            await using var tran = await conn.BeginTransactionAsync();

            try
            {
                // 1. Borcu kilitli oku
                var selectSql = @"
                    SELECT Id, Title, DebtType, BankAccountId, TotalAmount, RemainingAmount, PrincipalAmount, 
                       AccruedInterest, AccruedTax, MinPaymentAmount, TotalInstallments, RemainingInstallments, 
                       LastInterestCalculationDate, StartDate, Status
                FROM FinancialDebts WITH (UPDLOCK, ROWLOCK)
                WHERE Id = @id";

            FinancialDebt debt;
            await using (var cmd = conn.CreateCommand(selectSql, tran))
            {
                cmd.AddParam("id", debtId);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new Exception("Ödeme yapılacak borç kaydı bulunamadı.");

                debt = new FinancialDebt
                {
                    Id = Convert.ToInt32(reader[0]),
                    Title = reader.GetString(1),
                    DebtType = reader.GetString(2),
                    BankAccountId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    TotalAmount = reader.GetDecimal(4),
                    RemainingAmount = reader.GetDecimal(5),
                    PrincipalAmount = reader.GetDecimal(6),
                    AccruedInterest = reader.GetDecimal(7),
                    AccruedTax = reader.GetDecimal(8),
                    MinPaymentAmount = reader.GetDecimal(9),
                    TotalInstallments = reader.IsDBNull(10) ? 1 : Convert.ToInt32(reader[10]),
                    RemainingInstallments = reader.IsDBNull(11) ? 1 : Convert.ToInt32(reader[11]),
                    LastInterestCalculationDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                    StartDate = reader.GetDateTime(13),
                    Status = reader.GetString(14)
                };
            }

            // 2. Ödeme tarihine kadar faiz tahakkuk ettir (gerekirse)
            DateTime lastCalc = debt.LastInterestCalculationDate ?? debt.StartDate;
            if (paymentDate.Date > lastCalc.Date && debt.PrincipalAmount > 0)
            {
                await _interestEngine.AccrueInterestAsync(debtId, paymentDate, conn, tran, userName);

                // Güncellenmiş faiz/vergi tutarlarını yeniden oku
                await using var refreshCmd = conn.CreateCommand("SELECT PrincipalAmount, AccruedInterest, AccruedTax, RemainingAmount FROM FinancialDebts WHERE Id = @id", tran);
                refreshCmd.AddParam("id", debtId);
                await using var refReader = await refreshCmd.ExecuteReaderAsync();
                if (await refReader.ReadAsync())
                {
                    debt.PrincipalAmount = refReader.GetDecimal(0);
                    debt.AccruedInterest = refReader.GetDecimal(1);
                    debt.AccruedTax = refReader.GetDecimal(2);
                    debt.RemainingAmount = refReader.GetDecimal(3);
                }
            }

            // 3. Dağıtım motorunu çalıştır
            var breakdown = PaymentAllocationService.Allocate(
                totalPaid: paymentAmount,
                accruedInterest: debt.AccruedInterest,
                accruedTax: debt.AccruedTax,
                currentPrincipal: debt.PrincipalAmount);

            // 4. Payments kaydı oluştur
            var paySql = @"
                INSERT INTO Payments (DebtId, BankAccountId, PaymentDate, TotalPaid, PaymentType, Notes, CreatedBy, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@did, @baid, @pdate, @paid, @ptype, @notes, @user, GETDATE());";

            await using var payCmd = conn.CreateCommand(paySql, tran);
            payCmd.AddParam("did", debtId);
            payCmd.AddParam("baid", (object?)fromBankAccountId ?? DBNull.Value);
            payCmd.AddParam("pdate", paymentDate);
            payCmd.AddParam("paid", paymentAmount);
            payCmd.AddParam("ptype", paymentType);
            payCmd.AddParam("notes", (object?)notes ?? DBNull.Value);
            payCmd.AddParam("user", userName);

            long paymentId = Convert.ToInt64(await payCmd.ExecuteScalarAsync());

            // 5. PaymentAllocations kaydı
            var allocSql = @"
                INSERT INTO PaymentAllocations (PaymentId, DebtId, AllocatedInterest, AllocatedTax, AllocatedExpense, AllocatedPrincipal, RemainingPrincipalAfter, CreatedAt)
                VALUES (@pid, @did, @aint, @atax, @aexp, @aprin, @remPrin, GETDATE());";

            await using var allocCmd = conn.CreateCommand(allocSql, tran);
            allocCmd.AddParam("pid", paymentId);
            allocCmd.AddParam("did", debtId);
            allocCmd.AddParam("aint", breakdown.ToInterest);
            allocCmd.AddParam("atax", breakdown.ToTax);
            allocCmd.AddParam("aexp", breakdown.ToExpense);
            allocCmd.AddParam("aprin", breakdown.ToPrincipal);
            allocCmd.AddParam("remPrin", breakdown.RemainingPrincipal);
            await allocCmd.ExecuteNonQueryAsync();

            // 6. FinancialDebts güncelle
            bool isFullyClosed = breakdown.RemainingTotalDebt <= 0.01m;
            string newStatus = isFullyClosed ? "Tamamlandı / Kapandı" : "Devam Ediyor";
            int newRemainingInstallments = debt.RemainingInstallments;
            if (debt.TotalInstallments > 1 && newRemainingInstallments > 0)
            {
                newRemainingInstallments = Math.Max(0, newRemainingInstallments - 1);
            }

            var updateDebtSql = @"
                UPDATE FinancialDebts
                SET PrincipalAmount = @prin,
                    AccruedInterest = @aint,
                    AccruedTax = @atax,
                    RemainingAmount = @rem,
                    RemainingInstallments = @rinst,
                    Status = @stat
                WHERE Id = @id;";

            await using var updCmd = conn.CreateCommand(updateDebtSql, tran);
            updCmd.AddParam("prin", isFullyClosed ? 0m : breakdown.RemainingPrincipal);
            updCmd.AddParam("aint", isFullyClosed ? 0m : breakdown.RemainingInterest);
            updCmd.AddParam("atax", isFullyClosed ? 0m : breakdown.RemainingTax);
            updCmd.AddParam("rem", isFullyClosed ? 0m : breakdown.RemainingTotalDebt);
            updCmd.AddParam("rinst", newRemainingInstallments);
            updCmd.AddParam("stat", newStatus);
            updCmd.AddParam("id", debtId);
            await updCmd.ExecuteNonQueryAsync();

            // 7. Bağlı banka hesabı (Kredi Kartı veya KMH) güncellenmesi: Borç azalır, limit açılır
            if (debt.BankAccountId.HasValue)
            {
                var cardUpdSql = @"
                    UPDATE BankAccounts
                    SET CurrentDebt = CASE WHEN CurrentDebt - @paid < 0 THEN 0 ELSE CurrentDebt - @paid END,
                        AvailableLimit = AvailableLimit + @paid
                    WHERE Id = @baid;";

                await using var cardCmd = conn.CreateCommand(cardUpdSql, tran);
                cardCmd.AddParam("paid", paymentAmount);
                cardCmd.AddParam("baid", debt.BankAccountId.Value);
                await cardCmd.ExecuteNonQueryAsync();
            }

            // 8. Ödemenin çıktığı vadesiz banka hesabından para düşülmesi (veya nakit kasa)
            string paidFromDesc = "Nakit Kasa";
            if (fromBankAccountId.HasValue)
            {
                // Bankadan para çıkışı
                var getBankSql = "SELECT BankName, AccountName, CurrentBalance FROM BankAccounts WITH (UPDLOCK) WHERE Id = @baid";
                string bName = "", aName = "";
                decimal curBal = 0;
                await using (var bCmd = conn.CreateCommand(getBankSql, tran))
                {
                    bCmd.AddParam("baid", fromBankAccountId.Value);
                    await using var bReader = await bCmd.ExecuteReaderAsync();
                    if (await bReader.ReadAsync())
                    {
                        bName = bReader.GetString(0);
                        aName = bReader.GetString(1);
                        curBal = bReader.GetDecimal(2);
                    }
                }

                decimal newBal = curBal - paymentAmount;
                paidFromDesc = $"Banka: {bName} - {aName}";

                // BankAccounts bakiyesini güncelle
                var updBankSql = "UPDATE BankAccounts SET CurrentBalance = @newBal WHERE Id = @baid";
                await using var updBankCmd = conn.CreateCommand(updBankSql, tran);
                updBankCmd.AddParam("newBal", newBal);
                updBankCmd.AddParam("baid", fromBankAccountId.Value);
                await updBankCmd.ExecuteNonQueryAsync();

                // BankTransactions işlem kaydı ekle
                var bankTransSql = @"
                    INSERT INTO BankTransactions (CreatedAt, BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, ReferenceId, ReferenceType, ""User"")
                    VALUES (@dt, @baid, @baname, 'Borç / Finansman Ödemesi', 'Çıkış', 0, @outflow, @bal, @desc, 'Havale/EFT', @refId, 'DEBT_PAYMENT', @user);";

                await using var btCmd = conn.CreateCommand(bankTransSql, tran);
                btCmd.AddParam("dt", paymentDate);
                btCmd.AddParam("baid", fromBankAccountId.Value);
                btCmd.AddParam("baname", $"{bName} - {aName}");
                btCmd.AddParam("outflow", paymentAmount);
                btCmd.AddParam("bal", newBal);
                btCmd.AddParam("desc", $"{debt.Title} ödemesi (Anapara: ₺{breakdown.ToPrincipal:N2}, Faiz: ₺{breakdown.ToInterest:N2}, Vergi: ₺{breakdown.ToTax:N2})");
                btCmd.AddParam("refId", paymentId);
                btCmd.AddParam("user", userName);
                await btCmd.ExecuteNonQueryAsync();
            }

            // 9. İlgili FinancialReminders takvim hatırlatmasını 'Ödendi' yap
            var updRemSql = @"
                UPDATE FinancialReminders 
                SET Status = 'Ödendi',
                    IsPaid = true,
                    PaidDate = @pdate,
                    PaidFrom = @pfrom,
                    BankAccountId = @baid,
                    RemainingDebtAfter = @remAfter,
                    PrincipalPaid = @prinPaid,
                    AccruedInterest = @aintPaid,
                    TaxAmount = @taxPaid
                WHERE DebtId = @did AND (Status = 'Bekliyor' OR IsPaid = false) AND IsDeleted = false AND DueDate <= @pdate";

            await using var updRemCmd = conn.CreateCommand(updRemSql, tran);
            updRemCmd.AddParam("pdate", paymentDate);
            updRemCmd.AddParam("pfrom", paidFromDesc);
            updRemCmd.AddParam("baid", (object?)fromBankAccountId ?? DBNull.Value);
            updRemCmd.AddParam("remAfter", isFullyClosed ? 0m : breakdown.RemainingTotalDebt);
            updRemCmd.AddParam("prinPaid", breakdown.ToPrincipal);
            updRemCmd.AddParam("aintPaid", breakdown.ToInterest);
            updRemCmd.AddParam("taxPaid", breakdown.ToTax);
            updRemCmd.AddParam("did", debtId);
            await updRemCmd.ExecuteNonQueryAsync();

            // 10. DebtTransactions hareket kaydı ekle
            var dtTransSql = @"
                INSERT INTO DebtTransactions (DebtId, TransactionDate, TransactionType, Amount, PrincipalChange, InterestChange, TaxChange, PreviousBalance, NewBalance, Description, ReferenceId, ReferenceType)
                VALUES (@did, @dt, 'Ödeme', @amt, @pchange, @ichange, @tchange, @prev, @new, @desc, @refId, 'PAYMENT');";

            await using var dtCmd = conn.CreateCommand(dtTransSql, tran);
            dtCmd.AddParam("did", debtId);
            dtCmd.AddParam("dt", paymentDate);
            dtCmd.AddParam("amt", paymentAmount);
            dtCmd.AddParam("pchange", -breakdown.ToPrincipal);
            dtCmd.AddParam("ichange", -breakdown.ToInterest);
            dtCmd.AddParam("tchange", -breakdown.ToTax);
            dtCmd.AddParam("prev", debt.RemainingAmount);
            dtCmd.AddParam("new", isFullyClosed ? 0m : breakdown.RemainingTotalDebt);
            dtCmd.AddParam("desc", $"{paymentType} ödeme yapıldı ({paidFromDesc}). Dağıtım: Anapara ₺{breakdown.ToPrincipal:N2}, Faiz ₺{breakdown.ToInterest:N2}, Vergi ₺{breakdown.ToTax:N2}");
            dtCmd.AddParam("refId", paymentId);
            await dtCmd.ExecuteNonQueryAsync();

            // 11. Audit Log kaydı
            await _auditService.LogAsync(
                "PAYMENT",
                "FinancialDebt",
                debtId,
                $"{debt.Title} için ₺{paymentAmount:N2} ödeme yapıldı. Kalan Anapara: ₺{breakdown.RemainingPrincipal:N2}, Kalan Toplam Borç: ₺{breakdown.RemainingTotalDebt:N2}",
                debt.RemainingAmount.ToString(),
                breakdown.RemainingTotalDebt.ToString(),
                fromBankAccountId,
                debtId,
                null,
                userName,
                conn,
                tran);

            await tran.CommitAsync();

            _lastPaymentFingerprint = fp;
            _lastPaymentTime = DateTime.Now;
            _lastBreakdown = breakdown;

            return breakdown;
            }
            catch
            {
                await tran.RollbackAsync();
                throw;
            }
        }
        finally
        {
            _paymentLock.Release();
        }
    }
}
