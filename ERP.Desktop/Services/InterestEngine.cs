using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class InterestEngine
{
    private readonly DatabaseService _dbService;

    public InterestEngine(DatabaseService? dbService = null)
    {
        _dbService = dbService ?? new DatabaseService();
    }

    /// <summary>
    /// Faiz hesaplama matematiksel fonksiyonu:
    /// Net Faiz = Anapara * (Aylık Faiz Oranı / 100) * (Gün / 30)
    /// KKDF = Net Faiz * (%15)
    /// BSMV = Net Faiz * (%5)
    /// Toplam Vergi = KKDF + BSMV
    /// </summary>
    public static (decimal netInterest, decimal bsmv, decimal kkdf, decimal totalTax, decimal totalAccrual) Calculate(
        decimal principal,
        decimal monthlyRate,
        int days,
        decimal bsmvRate = 5.0m,
        decimal kkdfRate = 15.0m)
    {
        if (principal <= 0 || monthlyRate <= 0 || days <= 0)
            return (0m, 0m, 0m, 0m, 0m);

        decimal dailyRate = (monthlyRate / 100m) / 30.0m;
        decimal netInterest = Math.Round(principal * dailyRate * days, 2, MidpointRounding.AwayFromZero);
        decimal bsmv = Math.Round(netInterest * (bsmvRate / 100m), 2, MidpointRounding.AwayFromZero);
        decimal kkdf = Math.Round(netInterest * (kkdfRate / 100m), 2, MidpointRounding.AwayFromZero);
        decimal totalTax = bsmv + kkdf;
        decimal totalAccrual = netInterest + totalTax;

        return (netInterest, bsmv, kkdf, totalTax, totalAccrual);
    }

    /// <summary>
    /// Belirtilen borç türü veya hesap için geçerli faiz oranını veritabanından çeker.
    /// Tarihsel geçmişi korur: Geçmiş oranlar geriye dönük değişmez.
    /// </summary>
    public async Task<InterestRate?> GetEffectiveRateAsync(
        string accountType,
        string interestType,
        DateTime date,
        int? bankAccountId = null,
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
                SELECT TOP 1 Id, BankAccountId, AccountType, InterestType, Rate, EffectiveDate, EndDate, BsmvRate, KkdfRate, IsActive, CreatedAt
                FROM InterestRates
                WHERE AccountType = @atype
                  AND InterestType = @itype
                  AND EffectiveDate <= @dt
                  AND (EndDate IS NULL OR EndDate >= @dt)
                  AND (@baid IS NULL OR BankAccountId = @baid OR BankAccountId IS NULL)
                  AND IsActive = 1
                ORDER BY BankAccountId DESC, EffectiveDate DESC";

            await using var cmd = new SqlCommand(sql, conn, existingTran);
            cmd.Parameters.AddWithValue("@atype", accountType);
            cmd.Parameters.AddWithValue("@itype", interestType);
            cmd.Parameters.AddWithValue("@dt", date);
            cmd.Parameters.AddWithValue("@baid", (object?)bankAccountId ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new InterestRate
                {
                    Id = reader.GetInt32(0),
                    BankAccountId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    AccountType = reader.GetString(2),
                    InterestType = reader.GetString(3),
                    Rate = reader.GetDecimal(4),
                    EffectiveDate = reader.GetDateTime(5),
                    EndDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    BsmvRate = reader.GetDecimal(7),
                    KkdfRate = reader.GetDecimal(8),
                    IsActive = reader.GetBoolean(9),
                    CreatedAt = reader.GetDateTime(10)
                };
            }

            // Varsayılan oranlar
            decimal defRate = accountType switch
            {
                "Kredi Kartı" => interestType == "Gecikme Faizi" ? 4.75m : 4.25m,
                "KMH / Ek Hesap" => interestType == "Gecikme Faizi" ? 5.50m : 5.00m,
                _ => 3.75m
            };

            return new InterestRate
            {
                AccountType = accountType,
                InterestType = interestType,
                Rate = defRate,
                EffectiveDate = date,
                BsmvRate = 5.0m,
                KkdfRate = 15.0m,
                IsActive = true
            };
        }
        finally
        {
            if (closeConn && conn != null)
            {
                await conn.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Borç için faiz tahakkukunu idempotently hesaplar ve SQL Server'a kaydeder.
    /// DİKKAT: Faiz tahakkuku Banka Bakiyesini DÜŞMEZ. Sadece borç ve faiz tutarını artırır.
    /// </summary>
    public async Task<InterestAccrual?> AccrueInterestAsync(
        int debtId,
        DateTime toDate,
        SqlConnection conn,
        SqlTransaction tran,
        string userName = "Sistem")
    {
        // 1. Borç bilgisini al
        var debtSql = @"SELECT Id, Title, DebtType, BankAccountId, PrincipalAmount, RemainingAmount, AccruedInterest, AccruedTax, 
                               InterestRate, LastInterestCalculationDate, StartDate 
                        FROM FinancialDebts WHERE Id = @id";
        await using var debtCmd = new SqlCommand(debtSql, conn, tran);
        debtCmd.Parameters.AddWithValue("@id", debtId);

        decimal principal = 0;
        decimal accruedInt = 0;
        decimal accruedTax = 0;
        decimal rateVal = 0;
        string debtType = "Kredi / Borç";
        int? bankAccountId = null;
        DateTime? lastCalcDate = null;
        DateTime startDate = DateTime.Today;

        await using (var reader = await debtCmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                debtType = reader.GetString(2);
                bankAccountId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                principal = reader.GetDecimal(4);
                accruedInt = reader.GetDecimal(6);
                accruedTax = reader.GetDecimal(7);
                rateVal = reader.GetDecimal(8);
                lastCalcDate = reader.IsDBNull(9) ? null : reader.GetDateTime(9);
                startDate = reader.GetDateTime(10);
            }
            else
            {
                return null;
            }
        }

        if (principal <= 0) return null;

        DateTime fromDate = lastCalcDate ?? startDate;
        if (toDate.Date <= fromDate.Date) return null; // Sıfır veya negatif gün

        int days = (toDate.Date - fromDate.Date).Days;
        if (days <= 0) return null;

        // 2. Oranı bul
        if (rateVal <= 0)
        {
            var effRate = await GetEffectiveRateAsync(debtType, "Akdi Faiz", toDate, bankAccountId, conn, tran);
            rateVal = effRate?.Rate ?? 4.25m;
        }

        var (netInt, bsmv, kkdf, totTax, totalAccrual) = Calculate(principal, rateVal, days);
        if (totalAccrual <= 0) return null;

        // 3. InterestAccruals tablosuna ekle
        var accrualSql = @"
            INSERT INTO InterestAccruals (DebtId, FromDate, ToDate, Days, Rate, PrincipalAmount, CalculatedInterest, BsmvAmount, KkdfAmount, TotalTax, TotalAccrual, IsApplied, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@did, @fdate, @tdate, @days, @rate, @prin, @cint, @bsmv, @kkdf, @ttax, @tot, 1, GETDATE());";

        await using var accrualCmd = new SqlCommand(accrualSql, conn, tran);
        accrualCmd.Parameters.AddWithValue("@did", debtId);
        accrualCmd.Parameters.AddWithValue("@fdate", fromDate);
        accrualCmd.Parameters.AddWithValue("@tdate", toDate);
        accrualCmd.Parameters.AddWithValue("@days", days);
        accrualCmd.Parameters.AddWithValue("@rate", rateVal);
        accrualCmd.Parameters.AddWithValue("@prin", principal);
        accrualCmd.Parameters.AddWithValue("@cint", netInt);
        accrualCmd.Parameters.AddWithValue("@bsmv", bsmv);
        accrualCmd.Parameters.AddWithValue("@kkdf", kkdf);
        accrualCmd.Parameters.AddWithValue("@ttax", totTax);
        accrualCmd.Parameters.AddWithValue("@tot", totalAccrual);

        long accrualId = Convert.ToInt64(await accrualCmd.ExecuteScalarAsync());

        // 4. FinancialDebts güncelle (Borç artar, banka bakiyesi DÜŞMEZ!)
        decimal newAccruedInt = accruedInt + netInt;
        decimal newAccruedTax = accruedTax + totTax;
        decimal newRemaining = principal + newAccruedInt + newAccruedTax;

        var updateDebtSql = @"
            UPDATE FinancialDebts 
            SET AccruedInterest = @aint,
                AccruedTax = @atax,
                RemainingAmount = @rem,
                LastInterestCalculationDate = @calcDate
            WHERE Id = @id;";

        await using var updateDebtCmd = new SqlCommand(updateDebtSql, conn, tran);
        updateDebtCmd.Parameters.AddWithValue("@aint", newAccruedInt);
        updateDebtCmd.Parameters.AddWithValue("@atax", newAccruedTax);
        updateDebtCmd.Parameters.AddWithValue("@rem", newRemaining);
        updateDebtCmd.Parameters.AddWithValue("@calcDate", toDate);
        updateDebtCmd.Parameters.AddWithValue("@id", debtId);
        await updateDebtCmd.ExecuteNonQueryAsync();

        // 5. DebtTransactions hareket kaydı ekle
        var transSql = @"
            INSERT INTO DebtTransactions (DebtId, TransactionDate, TransactionType, Amount, PrincipalChange, InterestChange, TaxChange, PreviousBalance, NewBalance, Description, ReferenceId, ReferenceType)
            VALUES (@did, @dt, 'Faiz Tahakkuku', @amt, 0, @intChange, @taxChange, @prev, @new, @desc, @refId, 'INTEREST_ACCRUAL');";

        await using var transCmd = new SqlCommand(transSql, conn, tran);
        transCmd.Parameters.AddWithValue("@did", debtId);
        transCmd.Parameters.AddWithValue("@dt", toDate);
        transCmd.Parameters.AddWithValue("@amt", totalAccrual);
        transCmd.Parameters.AddWithValue("@intChange", netInt);
        transCmd.Parameters.AddWithValue("@taxChange", totTax);
        transCmd.Parameters.AddWithValue("@prev", principal + accruedInt + accruedTax);
        transCmd.Parameters.AddWithValue("@new", newRemaining);
        transCmd.Parameters.AddWithValue("@desc", $"{days} günlük faiz tahakkuku (%{rateVal:N2} oran, Net Faiz: ₺{netInt:N2}, Vergi: ₺{totTax:N2})");
        transCmd.Parameters.AddWithValue("@refId", accrualId);
        await transCmd.ExecuteNonQueryAsync();

        return new InterestAccrual
        {
            Id = accrualId,
            DebtId = debtId,
            FromDate = fromDate,
            ToDate = toDate,
            Days = days,
            Rate = rateVal,
            PrincipalAmount = principal,
            CalculatedInterest = netInt,
            BsmvAmount = bsmv,
            KkdfAmount = kkdf,
            TotalTax = totTax,
            TotalAccrual = totalAccrual,
            IsApplied = true
        };
    }
}
