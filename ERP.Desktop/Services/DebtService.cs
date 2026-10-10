using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

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
                await using var conn = new SqlConnection(_dbService.ConnectionString);
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

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@all", includeInactive ? 1 : 0);

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
                await using var conn = new SqlConnection(_dbService.ConnectionString);
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

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", id);

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
        await using var conn = new SqlConnection(_dbService.ConnectionString);
        await conn.OpenAsync();
        await using var tran = conn.BeginTransaction();

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

                await using var cmd = new SqlCommand(sql, conn, tran);
                cmd.Parameters.AddWithValue("@t", debt.Title);
                cmd.Parameters.AddWithValue("@cred", debt.Creditor);
                cmd.Parameters.AddWithValue("@dtype", debt.DebtType);
                cmd.Parameters.AddWithValue("@baid", (object?)debt.BankAccountId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@tot", debt.TotalAmount);
                cmd.Parameters.AddWithValue("@rem", debt.RemainingAmount);
                cmd.Parameters.AddWithValue("@prin", debt.PrincipalAmount);
                cmd.Parameters.AddWithValue("@aint", debt.AccruedInterest);
                cmd.Parameters.AddWithValue("@atax", debt.AccruedTax);
                cmd.Parameters.AddWithValue("@min", debt.MinPaymentAmount);
                cmd.Parameters.AddWithValue("@rate", debt.InterestRate);
                cmd.Parameters.AddWithValue("@lrate", debt.LateInterestRate);
                cmd.Parameters.AddWithValue("@inst", debt.InstallmentAmount);
                cmd.Parameters.AddWithValue("@tinst", debt.TotalInstallments);
                cmd.Parameters.AddWithValue("@rinst", debt.RemainingInstallments);
                cmd.Parameters.AddWithValue("@dom", debt.DayOfMonth);
                cmd.Parameters.AddWithValue("@sdate", debt.StartDate);
                cmd.Parameters.AddWithValue("@calc", (object?)debt.LastInterestCalculationDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cutoff", (object?)debt.CutoffDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@stat", debt.Status);
                cmd.Parameters.AddWithValue("@notes", (object?)debt.Notes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@act", debt.IsActive);

                debt.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                // İlk hareket
                var transSql = @"
                    INSERT INTO DebtTransactions (DebtId, TransactionDate, TransactionType, Amount, PrincipalChange, InterestChange, TaxChange, PreviousBalance, NewBalance, Description, ReferenceType)
                    VALUES (@did, @dt, 'Borç Başlangıcı', @amt, @prin, 0, 0, 0, @amt, @desc, 'INIT');";
                await using var transCmd = new SqlCommand(transSql, conn, tran);
                transCmd.Parameters.AddWithValue("@did", debt.Id);
                transCmd.Parameters.AddWithValue("@dt", debt.StartDate);
                transCmd.Parameters.AddWithValue("@amt", debt.TotalAmount);
                transCmd.Parameters.AddWithValue("@prin", debt.PrincipalAmount);
                transCmd.Parameters.AddWithValue("@desc", $"Borç / Kredi kaydı açıldı ({debt.Title})");
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

                await using var cmd = new SqlCommand(sql, conn, tran);
                cmd.Parameters.AddWithValue("@id", debt.Id);
                cmd.Parameters.AddWithValue("@t", debt.Title);
                cmd.Parameters.AddWithValue("@cred", debt.Creditor);
                cmd.Parameters.AddWithValue("@dtype", debt.DebtType);
                cmd.Parameters.AddWithValue("@baid", (object?)debt.BankAccountId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@tot", debt.TotalAmount);
                cmd.Parameters.AddWithValue("@rem", debt.RemainingAmount);
                cmd.Parameters.AddWithValue("@prin", debt.PrincipalAmount);
                cmd.Parameters.AddWithValue("@aint", debt.AccruedInterest);
                cmd.Parameters.AddWithValue("@atax", debt.AccruedTax);
                cmd.Parameters.AddWithValue("@min", debt.MinPaymentAmount);
                cmd.Parameters.AddWithValue("@rate", debt.InterestRate);
                cmd.Parameters.AddWithValue("@lrate", debt.LateInterestRate);
                cmd.Parameters.AddWithValue("@inst", debt.InstallmentAmount);
                cmd.Parameters.AddWithValue("@tinst", debt.TotalInstallments);
                cmd.Parameters.AddWithValue("@rinst", debt.RemainingInstallments);
                cmd.Parameters.AddWithValue("@dom", debt.DayOfMonth);
                cmd.Parameters.AddWithValue("@sdate", debt.StartDate);
                cmd.Parameters.AddWithValue("@calc", (object?)debt.LastInterestCalculationDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cutoff", (object?)debt.CutoffDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@stat", debt.Status);
                cmd.Parameters.AddWithValue("@notes", (object?)debt.Notes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@act", debt.IsActive);
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
        await using var conn = new SqlConnection(_dbService.ConnectionString);
        await conn.OpenAsync();
        await using var tran = conn.BeginTransaction();

        try
        {
            var sql = "UPDATE FinancialDebts SET IsActive = 0, Status = 'İptal / Silindi' WHERE Id = @id";
            await using var cmd = new SqlCommand(sql, conn, tran);
            cmd.Parameters.AddWithValue("@id", id);
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
        await using var conn = new SqlConnection(_dbService.ConnectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT Id, DebtId, TransactionDate, TransactionType, Amount, PrincipalChange, InterestChange, TaxChange, 
                   PreviousBalance, NewBalance, Description, ReferenceId, ReferenceType, UserId, CreatedAt
            FROM DebtTransactions
            WHERE DebtId = @did
            ORDER BY TransactionDate DESC, Id DESC";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@did", debtId);

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
        await using var conn = new SqlConnection(_dbService.ConnectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT Id, DebtId, FromDate, ToDate, Days, Rate, PrincipalAmount, CalculatedInterest, BsmvAmount, KkdfAmount, TotalTax, TotalAccrual, IsApplied, CreatedAt
            FROM InterestAccruals
            WHERE DebtId = @did
            ORDER BY ToDate DESC, Id DESC";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@did", debtId);

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
        await using var conn = new SqlConnection(_dbService.ConnectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT Id, DebtId, BankAccountId, PaymentDate, TotalPaid, PaymentType, Notes, CreatedBy, CreatedAt
            FROM Payments
            WHERE DebtId = @did
            ORDER BY PaymentDate DESC, Id DESC";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@did", debtId);

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
        await using var conn = new SqlConnection(_dbService.ConnectionString);
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

        await using var cmd = new SqlCommand(sql, conn);
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

        await using var conn = new SqlConnection(_dbService.ConnectionString);
        await conn.OpenAsync();
        await using var tran = conn.BeginTransaction();

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
            await using (var cmd = new SqlCommand(selectSql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@id", debtId);
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
                await using var refreshCmd = new SqlCommand("SELECT PrincipalAmount, AccruedInterest, AccruedTax, RemainingAmount FROM FinancialDebts WHERE Id = @id", conn, tran);
                refreshCmd.Parameters.AddWithValue("@id", debtId);
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

            await using var payCmd = new SqlCommand(paySql, conn, tran);
            payCmd.Parameters.AddWithValue("@did", debtId);
            payCmd.Parameters.AddWithValue("@baid", (object?)fromBankAccountId ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@pdate", paymentDate);
            payCmd.Parameters.AddWithValue("@paid", paymentAmount);
            payCmd.Parameters.AddWithValue("@ptype", paymentType);
            payCmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@user", userName);

            long paymentId = Convert.ToInt64(await payCmd.ExecuteScalarAsync());

            // 5. PaymentAllocations kaydı
            var allocSql = @"
                INSERT INTO PaymentAllocations (PaymentId, DebtId, AllocatedInterest, AllocatedTax, AllocatedExpense, AllocatedPrincipal, RemainingPrincipalAfter, CreatedAt)
                VALUES (@pid, @did, @aint, @atax, @aexp, @aprin, @remPrin, GETDATE());";

            await using var allocCmd = new SqlCommand(allocSql, conn, tran);
            allocCmd.Parameters.AddWithValue("@pid", paymentId);
            allocCmd.Parameters.AddWithValue("@did", debtId);
            allocCmd.Parameters.AddWithValue("@aint", breakdown.ToInterest);
            allocCmd.Parameters.AddWithValue("@atax", breakdown.ToTax);
            allocCmd.Parameters.AddWithValue("@aexp", breakdown.ToExpense);
            allocCmd.Parameters.AddWithValue("@aprin", breakdown.ToPrincipal);
            allocCmd.Parameters.AddWithValue("@remPrin", breakdown.RemainingPrincipal);
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

            await using var updCmd = new SqlCommand(updateDebtSql, conn, tran);
            updCmd.Parameters.AddWithValue("@prin", isFullyClosed ? 0m : breakdown.RemainingPrincipal);
            updCmd.Parameters.AddWithValue("@aint", isFullyClosed ? 0m : breakdown.RemainingInterest);
            updCmd.Parameters.AddWithValue("@atax", isFullyClosed ? 0m : breakdown.RemainingTax);
            updCmd.Parameters.AddWithValue("@rem", isFullyClosed ? 0m : breakdown.RemainingTotalDebt);
            updCmd.Parameters.AddWithValue("@rinst", newRemainingInstallments);
            updCmd.Parameters.AddWithValue("@stat", newStatus);
            updCmd.Parameters.AddWithValue("@id", debtId);
            await updCmd.ExecuteNonQueryAsync();

            // 7. Bağlı banka hesabı (Kredi Kartı veya KMH) güncellenmesi: Borç azalır, limit açılır
            if (debt.BankAccountId.HasValue)
            {
                var cardUpdSql = @"
                    UPDATE BankAccounts
                    SET CurrentDebt = CASE WHEN CurrentDebt - @paid < 0 THEN 0 ELSE CurrentDebt - @paid END,
                        AvailableLimit = AvailableLimit + @paid
                    WHERE Id = @baid;";

                await using var cardCmd = new SqlCommand(cardUpdSql, conn, tran);
                cardCmd.Parameters.AddWithValue("@paid", paymentAmount);
                cardCmd.Parameters.AddWithValue("@baid", debt.BankAccountId.Value);
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
                await using (var bCmd = new SqlCommand(getBankSql, conn, tran))
                {
                    bCmd.Parameters.AddWithValue("@baid", fromBankAccountId.Value);
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
                await using var updBankCmd = new SqlCommand(updBankSql, conn, tran);
                updBankCmd.Parameters.AddWithValue("@newBal", newBal);
                updBankCmd.Parameters.AddWithValue("@baid", fromBankAccountId.Value);
                await updBankCmd.ExecuteNonQueryAsync();

                // BankTransactions işlem kaydı ekle
                var bankTransSql = @"
                    INSERT INTO BankTransactions (CreatedAt, BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, ReferenceId, ReferenceType, [User])
                    VALUES (@dt, @baid, @baname, 'Borç / Finansman Ödemesi', 'Çıkış', 0, @outflow, @bal, @desc, 'Havale/EFT', @refId, 'DEBT_PAYMENT', @user);";

                await using var btCmd = new SqlCommand(bankTransSql, conn, tran);
                btCmd.Parameters.AddWithValue("@dt", paymentDate);
                btCmd.Parameters.AddWithValue("@baid", fromBankAccountId.Value);
                btCmd.Parameters.AddWithValue("@baname", $"{bName} - {aName}");
                btCmd.Parameters.AddWithValue("@outflow", paymentAmount);
                btCmd.Parameters.AddWithValue("@bal", newBal);
                btCmd.Parameters.AddWithValue("@desc", $"{debt.Title} ödemesi (Anapara: ₺{breakdown.ToPrincipal:N2}, Faiz: ₺{breakdown.ToInterest:N2}, Vergi: ₺{breakdown.ToTax:N2})");
                btCmd.Parameters.AddWithValue("@refId", paymentId);
                btCmd.Parameters.AddWithValue("@user", userName);
                await btCmd.ExecuteNonQueryAsync();
            }

            // 9. İlgili FinancialReminders takvim hatırlatmasını 'Ödendi' yap
            var updRemSql = @"
                UPDATE FinancialReminders 
                SET Status = 'Ödendi',
                    IsPaid = 1,
                    PaidAt = @pdate,
                    PaidDate = @pdate,
                    PaidFrom = @pfrom,
                    BankAccountId = @baid,
                    RemainingDebtAfter = @remAfter,
                    PrincipalPaid = @prinPaid,
                    AccruedInterest = @aintPaid,
                    TaxAmount = @taxPaid
                WHERE DebtId = @did AND (Status = 'Bekliyor' OR IsPaid = 0) AND IsDeleted = 0 AND DueDate <= @pdate";

            await using var updRemCmd = new SqlCommand(updRemSql, conn, tran);
            updRemCmd.Parameters.AddWithValue("@pdate", paymentDate);
            updRemCmd.Parameters.AddWithValue("@pfrom", paidFromDesc);
            updRemCmd.Parameters.AddWithValue("@baid", (object?)fromBankAccountId ?? DBNull.Value);
            updRemCmd.Parameters.AddWithValue("@remAfter", isFullyClosed ? 0m : breakdown.RemainingTotalDebt);
            updRemCmd.Parameters.AddWithValue("@prinPaid", breakdown.ToPrincipal);
            updRemCmd.Parameters.AddWithValue("@aintPaid", breakdown.ToInterest);
            updRemCmd.Parameters.AddWithValue("@taxPaid", breakdown.ToTax);
            updRemCmd.Parameters.AddWithValue("@did", debtId);
            await updRemCmd.ExecuteNonQueryAsync();

            // 10. DebtTransactions hareket kaydı ekle
            var dtTransSql = @"
                INSERT INTO DebtTransactions (DebtId, TransactionDate, TransactionType, Amount, PrincipalChange, InterestChange, TaxChange, PreviousBalance, NewBalance, Description, ReferenceId, ReferenceType)
                VALUES (@did, @dt, 'Ödeme', @amt, @pchange, @ichange, @tchange, @prev, @new, @desc, @refId, 'PAYMENT');";

            await using var dtCmd = new SqlCommand(dtTransSql, conn, tran);
            dtCmd.Parameters.AddWithValue("@did", debtId);
            dtCmd.Parameters.AddWithValue("@dt", paymentDate);
            dtCmd.Parameters.AddWithValue("@amt", paymentAmount);
            dtCmd.Parameters.AddWithValue("@pchange", -breakdown.ToPrincipal);
            dtCmd.Parameters.AddWithValue("@ichange", -breakdown.ToInterest);
            dtCmd.Parameters.AddWithValue("@tchange", -breakdown.ToTax);
            dtCmd.Parameters.AddWithValue("@prev", debt.RemainingAmount);
            dtCmd.Parameters.AddWithValue("@new", isFullyClosed ? 0m : breakdown.RemainingTotalDebt);
            dtCmd.Parameters.AddWithValue("@desc", $"{paymentType} ödeme yapıldı ({paidFromDesc}). Dağıtım: Anapara ₺{breakdown.ToPrincipal:N2}, Faiz ₺{breakdown.ToInterest:N2}, Vergi ₺{breakdown.ToTax:N2}");
            dtCmd.Parameters.AddWithValue("@refId", paymentId);
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
            return breakdown;
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }
}
