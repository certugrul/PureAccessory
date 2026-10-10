using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using System.Data.Common;

namespace ERP.Desktop.Services;

public class ReminderService
{
    private readonly InvoiceService _invoiceService;
    private readonly DatabaseService _dbService;
    private readonly DebtService _debtService;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string RemindersFile = Path.Combine(StorageDirectory, "financial_reminders.json");
    private static readonly string ExpensesFile = Path.Combine(StorageDirectory, "general_expenses.json");
    private static readonly string DebtsFile = Path.Combine(StorageDirectory, "financial_debts.json");
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public ReminderService(InvoiceService? invoiceService = null, DatabaseService? dbService = null, DebtService? debtService = null)
    {
        _invoiceService = invoiceService ?? new InvoiceService();
        _dbService = dbService ?? new DatabaseService();
        _debtService = debtService ?? new DebtService(_dbService);
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

    #region Genel Giderler (Sürekli / Düzenli Masraflar)

    public async Task<List<GeneralExpense>> GetAllGeneralExpensesAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadExpensesInternalAsync();
            if (list.Count == 0 && !File.Exists(ExpensesFile))
            {
                list = CreateDefaultExpenses();
                await SaveExpensesInternalAsync(list);
            }
            return list;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<GeneralExpense> SaveOrUpdateGeneralExpenseAsync(GeneralExpense expense)
    {
        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadExpensesInternalAsync();
            if (expense.Id == 0)
            {
                expense.Id = list.Count > 0 ? list.Max(e => e.Id) + 1 : 1;
                list.Add(expense);
            }
            else
            {
                var idx = list.FindIndex(e => e.Id == expense.Id);
                if (idx >= 0) list[idx] = expense;
                else list.Add(expense);
            }

            await SaveExpensesInternalAsync(list);
            return expense;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteGeneralExpenseAsync(int id)
    {
        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadExpensesInternalAsync();
            var target = list.FirstOrDefault(e => e.Id == id);
            if (target != null)
            {
                list.Remove(target);
                await SaveExpensesInternalAsync(list);

                // Bu şablona ait ödenmemiş tüm takvim hatırlatmalarını da temizle
                var reminders = await LoadRemindersInternalAsync();
                bool changed = false;
                foreach (var r in reminders.Where(r => r.GeneralExpenseId == id && r.Status != "Ödendi"))
                {
                    r.IsDeleted = true;
                    r.Status = "Silindi";
                    changed = true;
                }
                if (changed)
                {
                    await SaveRemindersInternalAsync(reminders);
                }

                return true;
            }
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ResetDefaultGeneralExpensesAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var list = CreateDefaultExpenses();
            await SaveExpensesInternalAsync(list);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static List<GeneralExpense> CreateDefaultExpenses()
    {
        return new List<GeneralExpense>
        {
            new GeneralExpense { Id = 1, Title = "İşyeri / Dükkan Kirası", Category = "Kira", MonthlyAmount = 25000m, DayOfMonth = 15, Description = "Aylık İşyeri / Mağaza Kira Bedeli" },
            new GeneralExpense { Id = 2, Title = "Elektrik & Su Faturaları", Category = "Fatura", MonthlyAmount = 3400m, DayOfMonth = 10, Description = "İşyeri Elektrik, Su ve Doğalgaz Giderleri" },
            new GeneralExpense { Id = 3, Title = "İnternet & Telefon Faturası", Category = "Fatura", MonthlyAmount = 850m, DayOfMonth = 20, Description = "İşyeri Sabit Hat ve Fiber İnternet Gideri" },
            new GeneralExpense { Id = 4, Title = "SGK Primi & Personel Avansları", Category = "Maaş/SGK", MonthlyAmount = 18500m, DayOfMonth = 25, Description = "Aylık Personel Maaş Avansı ve SGK Primleri" },
            new GeneralExpense { Id = 5, Title = "Vergi & KDV Tahakkuku", Category = "Vergi", MonthlyAmount = 7200m, DayOfMonth = 28, Description = "Aylık KDV / Muhtasar Beyanname Ödemesi" },
            new GeneralExpense { Id = 6, Title = "Muhasebe & Danışmanlık Ücreti", Category = "Diğer", MonthlyAmount = 2500m, DayOfMonth = 8, Description = "Aylık Mali Müşavir ve Muhasebe Hizmet Bedeli" }
        };
    }

    private async Task<List<GeneralExpense>> LoadExpensesInternalAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<GeneralExpense>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = "SELECT Id, Title, Category, MonthlyAmount, DayOfMonth, Notes, IsActive FROM GeneralExpenses WHERE IsActive = 1 ORDER BY DayOfMonth ASC";
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new GeneralExpense
                    {
                        Id = reader.GetInt32(0),
                        Title = reader.GetString(1),
                        Category = reader.GetString(2),
                        MonthlyAmount = reader.GetDecimal(3),
                        DayOfMonth = reader.GetInt32(4),
                        Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                        IsActive = reader.GetBoolean(6)
                    });
                }
                if (list.Count > 0) return list;
            }
        }
        catch { }

        if (!File.Exists(ExpensesFile)) return new List<GeneralExpense>();
        try
        {
            var json = await File.ReadAllTextAsync(ExpensesFile);
            return JsonSerializer.Deserialize<List<GeneralExpense>>(json) ?? new List<GeneralExpense>();
        }
        catch
        {
            return new List<GeneralExpense>();
        }
    }

    private async Task SaveExpensesInternalAsync(List<GeneralExpense> list)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var exp in list)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM GeneralExpenses WHERE Id = @id");
                    checkCmd.AddParam("id", exp.Id);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (exists > 0)
                    {
                        var updSql = "UPDATE GeneralExpenses SET Title=@t, Category=@c, MonthlyAmount=@amt, DayOfMonth=@dom, Notes=@notes, IsActive=@act WHERE Id=@id";
                        await using var cmd = conn.CreateCommand(updSql);
                        cmd.AddParam("id", exp.Id);
                        cmd.AddParam("t", exp.Title);
                        cmd.AddParam("c", exp.Category);
                        cmd.AddParam("amt", exp.MonthlyAmount);
                        cmd.AddParam("dom", exp.DayOfMonth);
                        cmd.AddParam("notes", (object?)exp.Description ?? DBNull.Value);
                        cmd.AddParam("act", exp.IsActive);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var insSql = @"INSERT INTO GeneralExpenses (Id, Title, Category, MonthlyAmount, DayOfMonth, Notes, IsActive)
                                       VALUES (@id, @t, @c, @amt, @dom, @notes, @act);";
                        await using var cmd = conn.CreateCommand(insSql);
                        cmd.AddParam("id", exp.Id);
                        cmd.AddParam("t", exp.Title);
                        cmd.AddParam("c", exp.Category);
                        cmd.AddParam("amt", exp.MonthlyAmount);
                        cmd.AddParam("dom", exp.DayOfMonth);
                        cmd.AddParam("notes", (object?)exp.Description ?? DBNull.Value);
                        cmd.AddParam("act", exp.IsActive);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
        catch { }

        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ExpensesFile, json);
    }

    #endregion

    #region Vadeli Borçlar & Krediler (Taksitli / Azalan Borçlar)

    public async Task<List<FinancialDebt>> GetAllDebtsAsync()
    {
        return await _debtService.GetAllDebtsAsync(false);
    }

    public async Task<FinancialDebt> SaveOrUpdateDebtAsync(FinancialDebt debt)
    {
        return await _debtService.SaveOrUpdateDebtAsync(debt);
    }

    public async Task<bool> DeleteDebtAsync(int id)
    {
        bool res = await _debtService.DeleteDebtAsync(id);
        if (res)
        {
            // Bu borca ait ödenmemiş tüm takvim hatırlatmalarını temizle
            var reminders = await LoadRemindersInternalAsync();
            bool changed = false;
            foreach (var r in reminders.Where(r => r.DebtId == id && r.Status != "Ödendi"))
            {
                r.IsDeleted = true;
                r.Status = "Silindi";
                changed = true;
            }
            if (changed)
            {
                await SaveRemindersInternalAsync(reminders);
            }
        }
        return res;
    }

    #endregion

    #region Takvim ve Hatırlatma Entegrasyonu

    public async Task<List<FinancialReminder>> GetAllRemindersAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadRemindersInternalAsync();
            return list.Where(r => !r.IsDeleted && r.Status != "Silindi").ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<FinancialReminder>> GetRemindersForMonthAsync(int year, int month)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);

        DateTime CalculateDueDateWithWeekendShift(int day)
        {
            var dt = new DateTime(year, month, Math.Clamp(day, 1, daysInMonth));
            if (dt.DayOfWeek == DayOfWeek.Saturday)
            {
                dt = dt.AddDays(2);
            }
            else if (dt.DayOfWeek == DayOfWeek.Sunday)
            {
                dt = dt.AddDays(1);
            }
            return dt;
        }

        var expenses = await GetAllGeneralExpensesAsync();
        var debts = await GetAllDebtsAsync();

        await _semaphore.WaitAsync();
        try
        {
            var fullList = await LoadRemindersInternalAsync();
            long maxId = fullList.Count > 0 ? fullList.Max(x => x.Id) : 0;
            bool changed = false;

            foreach (var r in fullList.Where(r => !r.IsDeleted && r.Status != "Ödendi" && r.Status != "Silindi"))
            {
                if (r.DueDate.DayOfWeek == DayOfWeek.Saturday)
                {
                    r.DueDate = r.DueDate.AddDays(2);
                    changed = true;
                }
                else if (r.DueDate.DayOfWeek == DayOfWeek.Sunday)
                {
                    r.DueDate = r.DueDate.AddDays(1);
                    changed = true;
                }
            }

            // 1. GENEL GİDERLER: Her ay düzenli takvimde yer alır
            foreach (var exp in expenses.Where(e => e.IsActive))
            {
                bool exists = fullList.Any(r => r.DueDate.Year == year && r.DueDate.Month == month && r.GeneralExpenseId == exp.Id);
                if (!exists)
                {
                    fullList.Add(new FinancialReminder
                    {
                        Id = ++maxId,
                        Title = exp.Title,
                        Category = exp.Category,
                        ReminderType = "Genel Gider",
                        GeneralExpenseId = exp.Id,
                        Amount = exp.MonthlyAmount,
                        DayOfMonth = exp.DayOfMonth,
                        DueDate = CalculateDueDateWithWeekendShift(exp.DayOfMonth),
                        IsRecurringMonthly = true,
                        Status = "Bekliyor",
                        Description = exp.Description ?? "Aylık Düzenli Genel Gider"
                    });
                    changed = true;
                }
            }

            // 2. BORÇLAR: SADECE Kalan Borç > 0 olan ve tamamlanmamış borçlar
            foreach (var debt in debts.Where(d => d.IsActive && !d.IsCompleted && d.RemainingAmount > 0))
            {
                bool exists = fullList.Any(r => r.DueDate.Year == year && r.DueDate.Month == month && r.DebtId == debt.Id);
                if (!exists)
                {
                    decimal installment = Math.Min(debt.InstallmentAmount > 0 ? debt.InstallmentAmount : debt.RemainingAmount, debt.RemainingAmount);
                    fullList.Add(new FinancialReminder
                    {
                        Id = ++maxId,
                        Title = $"{debt.Title} (Taksit: ₺{installment:N0} / Kalan: ₺{debt.RemainingAmount:N0})",
                        Category = debt.DebtType == "Kredi Kartı" ? "Kredi Kartı" : "Borç / Kredi",
                        ReminderType = "Borç / Kredi",
                        DebtId = debt.Id,
                        Amount = installment,
                        DayOfMonth = debt.DayOfMonth,
                        DueDate = CalculateDueDateWithWeekendShift(debt.DayOfMonth),
                        IsRecurringMonthly = true,
                        Status = "Bekliyor",
                        Description = $"{debt.Creditor} — Kalan Borç: ₺{debt.RemainingAmount:N2} ({debt.RemainingInstallments} Taksit Kaldı)"
                    });
                    changed = true;
                }
            }

            if (changed)
            {
                await SaveRemindersInternalAsync(fullList);
            }

            return fullList.Where(r => !r.IsDeleted && r.Status != "Silindi" && r.DueDate.Year == year && r.DueDate.Month == month).OrderBy(r => r.DueDate).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<FinancialReminder> SaveOrUpdateReminderAsync(FinancialReminder reminder)
    {
        if (reminder.DueDate.DayOfWeek == DayOfWeek.Saturday)
        {
            reminder.DueDate = reminder.DueDate.AddDays(2);
        }
        else if (reminder.DueDate.DayOfWeek == DayOfWeek.Sunday)
        {
            reminder.DueDate = reminder.DueDate.AddDays(1);
        }
        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadRemindersInternalAsync();
            if (reminder.Id == 0)
            {
                reminder.Id = list.Count > 0 ? list.Max(r => r.Id) + 1 : 1;
                list.Add(reminder);
            }
            else
            {
                var idx = list.FindIndex(r => r.Id == reminder.Id);
                if (idx >= 0) list[idx] = reminder;
                else list.Add(reminder);
            }

            await SaveRemindersInternalAsync(list);
            return reminder;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteReminderAsync(long id)
    {
        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadRemindersInternalAsync();
            var target = list.FirstOrDefault(r => r.Id == id);
            if (target != null)
            {
                target.IsDeleted = true;
                target.Status = "Silindi";
                await SaveRemindersInternalAsync(list);
                return true;
            }
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<FinancialReminder?> MarkAsPaidAsync(
        long reminderId,
        string paymentSource, // "Banka Hesabı" or "Nakit"
        int? bankAccountId,
        string? bankAccountName,
        DateTime paymentDate,
        string? notes)
    {
        FinancialReminder? reminder = null;

        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadRemindersInternalAsync();
            reminder = list.FirstOrDefault(r => r.Id == reminderId);
            if (reminder == null) return null;

            // Eğer bir Borç ise: DebtService ile atomik ödeme yap
            if (reminder.DebtId.HasValue && reminder.DebtId.Value > 0)
            {
                try
                {
                    var breakdown = await _debtService.ProcessPaymentAsync(
                        debtId: reminder.DebtId.Value,
                        paymentAmount: reminder.Amount,
                        paymentType: "Taksit",
                        fromBankAccountId: paymentSource == "Banka Hesabı" ? bankAccountId : null,
                        paymentDate: paymentDate,
                        notes: notes ?? $"{reminder.Title} Taksit Ödemesi",
                        userName: "Yönetici");

                    reminder.Status = "Ödendi";
                    reminder.PaidDate = paymentDate;
                    reminder.PaidFrom = paymentSource == "Banka Hesabı" ? $"Banka: {bankAccountName}" : "Merkez Nakit Kasa";
                    reminder.BankAccountId = bankAccountId;
                    reminder.RemainingDebtAfter = breakdown.RemainingTotalDebt;
                    reminder.PrincipalPaid = breakdown.ToPrincipal;
                    reminder.AccruedInterest = breakdown.ToInterest;
                    reminder.TaxAmount = breakdown.ToTax;

                    await SaveRemindersInternalAsync(list);
                    return reminder;
                }
                catch
                {
                    // Hata durumunda devam et
                }
            }

            // Genel Gider ise: Masraf Faturası Oluştur
            string invoiceTitle = reminder.DebtId.HasValue ? $"{reminder.Title} Taksit Ödemesi" : reminder.Title;
            var invoice = await _invoiceService.CreateExpenseInvoiceAsync(
                title: invoiceTitle,
                amount: reminder.Amount,
                category: reminder.Category,
                paymentType: paymentSource,
                bankAccountId: bankAccountId,
                bankAccountName: bankAccountName,
                invoiceDate: paymentDate,
                notes: notes ?? $"{reminder.Title} Ödemesi");

            reminder.Status = "Ödendi";
            reminder.PaidDate = paymentDate;
            reminder.PaidFrom = paymentSource == "Banka Hesabı" ? $"Banka: {bankAccountName}" : "Merkez Nakit Kasa";
            reminder.BankAccountId = bankAccountId;
            reminder.InvoiceNo = invoice.InvoiceNo;

            await SaveRemindersInternalAsync(list);
            return reminder;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SyncMonthRemindersAsync(int year, int month)
    {
        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadRemindersInternalAsync();
            list.RemoveAll(r => r.DueDate.Year == year && r.DueDate.Month == month && r.Status != "Ödendi" && (r.GeneralExpenseId.HasValue || r.DebtId.HasValue));
            await SaveRemindersInternalAsync(list);
        }
        finally
        {
            _semaphore.Release();
        }

        await GetRemindersForMonthAsync(year, month);
    }

    private async Task<List<FinancialReminder>> LoadRemindersInternalAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<FinancialReminder>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = @"
                    SELECT Id, Title, Category, ReminderType, GeneralExpenseId, DebtId, Amount, MinPayment, AccruedInterest, 
                           TaxAmount, PrincipalPaid, CurrentTotalDebt, InterestStartDate, DayOfMonth, DueDate, IsRecurringMonthly, 
                           Status, PaidDate, PaidFrom, BankAccountId, InvoiceNo, RemainingDebtAfter, Description, IsDeleted 
                    FROM FinancialReminders
                    ORDER BY DueDate ASC";

                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new FinancialReminder
                    {
                        Id = reader.GetInt64(0),
                        Title = reader.GetString(1),
                        Category = reader.GetString(2),
                        ReminderType = reader.GetString(3),
                        GeneralExpenseId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        DebtId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        Amount = reader.GetDecimal(6),
                        MinPayment = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                        AccruedInterest = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                        TaxAmount = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                        PrincipalPaid = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                        CurrentTotalDebt = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                        InterestStartDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                        DayOfMonth = reader.GetInt32(13),
                        DueDate = reader.GetDateTime(14),
                        IsRecurringMonthly = reader.GetBoolean(15),
                        Status = reader.GetString(16),
                        PaidDate = reader.IsDBNull(17) ? null : reader.GetDateTime(17),
                        PaidFrom = reader.IsDBNull(18) ? null : reader.GetString(18),
                        BankAccountId = reader.IsDBNull(19) ? null : reader.GetInt32(19),
                        InvoiceNo = reader.IsDBNull(20) ? null : reader.GetString(20),
                        RemainingDebtAfter = reader.IsDBNull(21) ? null : reader.GetDecimal(21),
                        Description = reader.IsDBNull(22) ? null : reader.GetString(22),
                        IsDeleted = reader.GetBoolean(23)
                    });
                }
                if (list.Count > 0) return list;
            }
        }
        catch { }

        if (!File.Exists(RemindersFile)) return new List<FinancialReminder>();
        try
        {
            var json = await File.ReadAllTextAsync(RemindersFile);
            return JsonSerializer.Deserialize<List<FinancialReminder>>(json) ?? new List<FinancialReminder>();
        }
        catch
        {
            return new List<FinancialReminder>();
        }
    }

    private async Task SaveRemindersInternalAsync(List<FinancialReminder> list)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var r in list)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM FinancialReminders WHERE Id = @id");
                    checkCmd.AddParam("id", r.Id);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (exists > 0)
                    {
                        var updSql = @"
                            UPDATE FinancialReminders 
                            SET Title=@t, Category=@c, ReminderType=@rtype, GeneralExpenseId=@geid, DebtId=@did, Amount=@amt, 
                                MinPayment=@min, AccruedInterest=@aint, TaxAmount=@tax, PrincipalPaid=@ppaid, CurrentTotalDebt=@ctot, 
                                InterestStartDate=@isdate, DayOfMonth=@dom, DueDate=@ddate, IsRecurringMonthly=@rec, Status=@stat, 
                                PaidDate=@pdate, PaidFrom=@pfrom, BankAccountId=@baid, InvoiceNo=@invno, RemainingDebtAfter=@rem, 
                                Description=@desc, IsDeleted=@del 
                            WHERE Id=@id;";
                        await using var cmd = conn.CreateCommand(updSql);
                        cmd.AddParam("id", r.Id);
                        cmd.AddParam("t", r.Title);
                        cmd.AddParam("c", r.Category);
                        cmd.AddParam("rtype", r.ReminderType);
                        cmd.AddParam("geid", (object?)r.GeneralExpenseId ?? DBNull.Value);
                        cmd.AddParam("did", (object?)r.DebtId ?? DBNull.Value);
                        cmd.AddParam("amt", r.Amount);
                        cmd.AddParam("min", (object?)r.MinPayment ?? DBNull.Value);
                        cmd.AddParam("aint", (object?)r.AccruedInterest ?? DBNull.Value);
                        cmd.AddParam("tax", (object?)r.TaxAmount ?? DBNull.Value);
                        cmd.AddParam("ppaid", (object?)r.PrincipalPaid ?? DBNull.Value);
                        cmd.AddParam("ctot", (object?)r.CurrentTotalDebt ?? DBNull.Value);
                        cmd.AddParam("isdate", (object?)r.InterestStartDate ?? DBNull.Value);
                        cmd.AddParam("dom", r.DayOfMonth);
                        cmd.AddParam("ddate", r.DueDate);
                        cmd.AddParam("rec", r.IsRecurringMonthly);
                        cmd.AddParam("stat", r.Status);
                        cmd.AddParam("pdate", (object?)r.PaidDate ?? DBNull.Value);
                        cmd.AddParam("pfrom", (object?)r.PaidFrom ?? DBNull.Value);
                        cmd.AddParam("baid", (object?)r.BankAccountId ?? DBNull.Value);
                        cmd.AddParam("invno", (object?)r.InvoiceNo ?? DBNull.Value);
                        cmd.AddParam("rem", (object?)r.RemainingDebtAfter ?? DBNull.Value);
                        cmd.AddParam("desc", (object?)r.Description ?? DBNull.Value);
                        cmd.AddParam("del", r.IsDeleted);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var insSql = @"
                            INSERT INTO FinancialReminders (Id, Title, Category, ReminderType, GeneralExpenseId, DebtId, Amount, 
                                                            MinPayment, AccruedInterest, TaxAmount, PrincipalPaid, CurrentTotalDebt, 
                                                            InterestStartDate, DayOfMonth, DueDate, IsRecurringMonthly, Status, 
                                                            PaidDate, PaidFrom, BankAccountId, InvoiceNo, RemainingDebtAfter, Description, IsDeleted)
                            VALUES (@id, @t, @c, @rtype, @geid, @did, @amt, @min, @aint, @tax, @ppaid, @ctot, @isdate, 
                                    @dom, @ddate, @rec, @stat, @pdate, @pfrom, @baid, @invno, @rem, @desc, @del);";
                        await using var cmd = conn.CreateCommand(insSql);
                        cmd.AddParam("id", r.Id);
                        cmd.AddParam("t", r.Title);
                        cmd.AddParam("c", r.Category);
                        cmd.AddParam("rtype", r.ReminderType);
                        cmd.AddParam("geid", (object?)r.GeneralExpenseId ?? DBNull.Value);
                        cmd.AddParam("did", (object?)r.DebtId ?? DBNull.Value);
                        cmd.AddParam("amt", r.Amount);
                        cmd.AddParam("min", (object?)r.MinPayment ?? DBNull.Value);
                        cmd.AddParam("aint", (object?)r.AccruedInterest ?? DBNull.Value);
                        cmd.AddParam("tax", (object?)r.TaxAmount ?? DBNull.Value);
                        cmd.AddParam("ppaid", (object?)r.PrincipalPaid ?? DBNull.Value);
                        cmd.AddParam("ctot", (object?)r.CurrentTotalDebt ?? DBNull.Value);
                        cmd.AddParam("isdate", (object?)r.InterestStartDate ?? DBNull.Value);
                        cmd.AddParam("dom", r.DayOfMonth);
                        cmd.AddParam("ddate", r.DueDate);
                        cmd.AddParam("rec", r.IsRecurringMonthly);
                        cmd.AddParam("stat", r.Status);
                        cmd.AddParam("pdate", (object?)r.PaidDate ?? DBNull.Value);
                        cmd.AddParam("pfrom", (object?)r.PaidFrom ?? DBNull.Value);
                        cmd.AddParam("baid", (object?)r.BankAccountId ?? DBNull.Value);
                        cmd.AddParam("invno", (object?)r.InvoiceNo ?? DBNull.Value);
                        cmd.AddParam("rem", (object?)r.RemainingDebtAfter ?? DBNull.Value);
                        cmd.AddParam("desc", (object?)r.Description ?? DBNull.Value);
                        cmd.AddParam("del", r.IsDeleted);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
        catch { }

        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(RemindersFile, json);
    }

    #endregion
}
