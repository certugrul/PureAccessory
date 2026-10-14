using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class BankService
{
    private readonly DatabaseService _dbService;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static bool _isInitialized = false;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string AccountsFile = Path.Combine(StorageDirectory, "bank_accounts.json");
    private static readonly string TransactionsFile = Path.Combine(StorageDirectory, "bank_transactions.json");
    private static readonly string PosFile = Path.Combine(StorageDirectory, "pos_transactions.json");

    public BankService(DatabaseService? dbService = null)
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

    #region Bank Accounts

    public async Task<List<BankAccount>> GetAllAccountsAsync(bool includeInactive = false)
    {
        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            return includeInactive ? accounts : accounts.Where(a => a.IsActive).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<BankAccount?> GetAccountByIdAsync(int id)
    {
        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            return accounts.FirstOrDefault(a => a.Id == id);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<BankAccount?> GetDefaultAccountAsync()
    {
        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            return accounts.FirstOrDefault(a => a.IsDefault && a.IsActive) 
                   ?? accounts.FirstOrDefault(a => a.IsActive);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<BankAccount> SaveOrUpdateAccountAsync(BankAccount account)
    {
        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();

            if (account.IsDefault)
            {
                foreach (var a in accounts)
                {
                    a.IsDefault = false;
                }
            }

            if (account.Id == 0)
            {
                account.Id = accounts.Count > 0 ? accounts.Max(a => a.Id) + 1 : 1;
                if (account.CurrentBalance == 0 && account.OpeningBalance != 0)
                {
                    account.CurrentBalance = account.OpeningBalance;
                }
                if (account.ActualBalance == 0)
                {
                    account.ActualBalance = account.CurrentBalance;
                }
                accounts.Add(account);

                // Açılış bakiyesi hareketi oluştur
                if (account.OpeningBalance > 0)
                {
                    var transactions = await LoadTransactionsInternalAsync();
                    transactions.Insert(0, new BankTransaction
                    {
                        Id = transactions.Count > 0 ? transactions.Max(t => t.Id) + 1 : 1,
                        CreatedAt = DateTime.Now,
                        BankAccountId = account.Id,
                        BankAccountName = $"{account.BankName} - {account.AccountName}",
                        TransactionType = "Açılış Bakiyesi",
                        Direction = "Giriş",
                        Inflow = account.OpeningBalance,
                        Outflow = 0,
                        BalanceAfter = account.OpeningBalance,
                        Description = "Hesap Açılış Bakiyesi",
                        PaymentMethod = "Diğer",
                        ReferenceType = "MANUEL",
                        User = "Yönetici"
                    });
                    await SaveTransactionsInternalAsync(transactions);
                }
            }
            else
            {
                var idx = accounts.FindIndex(a => a.Id == account.Id);
                if (idx >= 0)
                {
                    // Mevcut bakiyeyi koru
                    account.CurrentBalance = accounts[idx].CurrentBalance;
                    accounts[idx] = account;
                }
                else
                {
                    accounts.Add(account);
                }
            }

            await SaveAccountsInternalAsync(accounts);
            AppDataCache.UpdateBankAccounts(accounts);
            return account;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteAccountAsync(int accountId)
    {
        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            var target = accounts.FirstOrDefault(a => a.Id == accountId);
            if (target != null)
            {
                if (target.IsDefault)
                {
                    throw new InvalidOperationException("Varsayılan ana banka hesabı silinemez!");
                }
                target.IsActive = false; // Soft delete
                await SaveAccountsInternalAsync(accounts);
                AppDataCache.UpdateBankAccounts(accounts);
                return true;
            }
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    #endregion

    #region Bank Transactions

    public async Task<List<BankTransaction>> GetTransactionsAsync(
        int? accountId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? direction = null,
        string? transactionType = null,
        string? searchTerm = null)
    {
        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadTransactionsInternalAsync();
            var query = list.AsQueryable();

            if (accountId.HasValue && accountId.Value > 0)
            {
                query = query.Where(t => t.BankAccountId == accountId.Value);
            }

            if (startDate.HasValue)
            {
                var start = startDate.Value.Date;
                query = query.Where(t => t.CreatedAt.Date >= start);
            }

            if (endDate.HasValue)
            {
                var end = endDate.Value.Date;
                query = query.Where(t => t.CreatedAt.Date <= end);
            }

            if (!string.IsNullOrWhiteSpace(direction) && direction != "Tümü")
            {
                query = query.Where(t => t.Direction.Equals(direction, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(transactionType) && transactionType != "Tümü")
            {
                query = query.Where(t => t.TransactionType.Equals(transactionType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLowerInvariant();
                query = query.Where(t =>
                    t.Description.ToLowerInvariant().Contains(term) ||
                    t.BankAccountName.ToLowerInvariant().Contains(term) ||
                    t.PaymentMethod.ToLowerInvariant().Contains(term) ||
                    t.TransactionType.ToLowerInvariant().Contains(term));
            }

            return query.OrderByDescending(t => t.CreatedAt).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<BankTransaction> AddTransactionAsync(
        int accountId,
        string transactionType,
        string direction,
        decimal amount,
        string description,
        string paymentMethod = "Havale",
        long? referenceId = null,
        string? referenceType = "MANUEL",
        DateTime? customDate = null,
        string user = "Yönetici")
    {
        if (amount <= 0)
        {
            throw new ArgumentException("İşlem tutarı 0'dan büyük olmalıdır.", nameof(amount));
        }

        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            var account = accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null)
            {
                throw new InvalidOperationException($"Banka hesabı bulunamadı! (Id: {accountId})");
            }

            decimal inflow = 0;
            decimal outflow = 0;

            if (direction == "Giriş")
            {
                inflow = amount;
                account.CurrentBalance += amount;
            }
            else
            {
                outflow = amount;
                account.CurrentBalance -= amount;
            }

            var transactions = await LoadTransactionsInternalAsync();
            var tx = new BankTransaction
            {
                Id = transactions.Count > 0 ? transactions.Max(t => t.Id) + 1 : 1,
                CreatedAt = customDate ?? DateTime.Now,
                BankAccountId = account.Id,
                BankAccountName = $"{account.BankName} - {account.AccountName}",
                TransactionType = transactionType,
                Direction = direction,
                Inflow = inflow,
                Outflow = outflow,
                BalanceAfter = account.CurrentBalance,
                Description = description,
                PaymentMethod = paymentMethod,
                ReferenceId = referenceId,
                ReferenceType = referenceType,
                User = user
            };

            transactions.Insert(0, tx);

            // Veritabanına doğrudan tekil ekleme ve bakiye güncelleme (500+ kaydı tek tek turlamak yerine tekil ekler)
            try
            {
                if (await _dbService.TestConnectionAsync())
                {
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();

                    var insertSql = @"
                        INSERT INTO BankTransactions (Id, CreatedAt, BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, ReferenceId, ReferenceType, ""User"")
                        VALUES (@id, @created, @baid, @baname, @type, @dir, @inflow, @outflow, @bal, @desc, @pm, @refId, @refType, @user);";
                    await using var cmd = conn.CreateCommand(insertSql);
                    cmd.AddParam("id", tx.Id);
                    cmd.AddParam("created", tx.CreatedAt);
                    cmd.AddParam("baid", tx.BankAccountId);
                    cmd.AddParam("baname", tx.BankAccountName);
                    cmd.AddParam("type", tx.TransactionType);
                    cmd.AddParam("dir", tx.Direction);
                    cmd.AddParam("inflow", tx.Inflow);
                    cmd.AddParam("outflow", tx.Outflow);
                    cmd.AddParam("bal", tx.BalanceAfter);
                    cmd.AddParam("desc", tx.Description);
                    cmd.AddParam("pm", tx.PaymentMethod);
                    cmd.AddParam("refId", tx.ReferenceId);
                    cmd.AddParam("refType", tx.ReferenceType);
                    cmd.AddParam("user", tx.User);
                    await cmd.ExecuteNonQueryAsync();

                    var updateAccSql = "UPDATE BankAccounts SET CurrentBalance=@curBal, ActualBalance=@curBal WHERE Id=@id";
                    await using var cmdAcc = conn.CreateCommand(updateAccSql);
                    cmdAcc.AddParam("curBal", account.CurrentBalance);
                    cmdAcc.AddParam("id", account.Id);
                    await cmdAcc.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BankService] AddTransactionAsync SQL hatası: {ex.Message}");
            }

            try
            {
                EnsureStorageDirectory();
                var jsonAcc = JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(AccountsFile, jsonAcc);

                var jsonTx = JsonSerializer.Serialize(transactions, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(TransactionsFile, jsonTx);
            }
            catch { }

            return tx;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteTransactionAsync(long transactionId)
    {
        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var transactions = await LoadTransactionsInternalAsync();
            var tx = transactions.FirstOrDefault(t => t.Id == transactionId);
            if (tx == null) return false;

            // İlgili banka hesabının bakiyesini tersine çevirerek düzelt
            var accounts = await LoadAccountsInternalAsync();
            var account = accounts.FirstOrDefault(a => a.Id == tx.BankAccountId);
            if (account != null)
            {
                if (tx.Direction == "Giriş")
                {
                    account.CurrentBalance -= tx.Inflow;
                }
                else if (tx.Direction == "Çıkış")
                {
                    account.CurrentBalance += tx.Outflow;
                }
                await SaveAccountsInternalAsync(accounts);
            }

            transactions.Remove(tx);

            try
            {
                if (await _dbService.TestConnectionAsync())
                {
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();
                    var delSql = "DELETE FROM BankTransactions WHERE Id = @id";
                    await using var cmd = conn.CreateCommand(delSql);
                    cmd.AddParam("id", transactionId);
                    await cmd.ExecuteNonQueryAsync();
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
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    #endregion

    #region POS Transactions

    public async Task<List<PosTransaction>> GetPosTransactionsAsync(
        string? status = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? dateFilterMode = null)
    {
        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadPosTransactionsInternalAsync();
            var query = list.AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && status != "Tümü")
            {
                if (status == "Gecikti")
                {
                    query = query.Where(p => p.Status != "Ödendi" && p.ExpectedDate.Date < DateTime.Today);
                }
                else if (status == "Vadesi Bugün")
                {
                    query = query.Where(p => p.Status != "Ödendi" && p.ExpectedDate.Date == DateTime.Today);
                }
                else
                {
                    query = query.Where(p => p.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (!string.IsNullOrWhiteSpace(dateFilterMode))
            {
                if (dateFilterMode == "DueToday")
                {
                    query = query.Where(p => p.ExpectedDate.Date == DateTime.Today);
                }
                else if (dateFilterMode == "SoldToday")
                {
                    query = query.Where(p => p.SaleDate.Date == DateTime.Today);
                }
                else if (dateFilterMode == "ThisWeek")
                {
                    var today = DateTime.Today;
                    var startOfWeek = today.AddDays(-((int)today.DayOfWeek == 0 ? 6 : (int)today.DayOfWeek - 1));
                    var endOfWeek = startOfWeek.AddDays(6);
                    query = query.Where(p => p.SaleDate.Date >= startOfWeek && p.SaleDate.Date <= endOfWeek);
                }
                else if (dateFilterMode == "ThisMonth")
                {
                    var today = DateTime.Today;
                    query = query.Where(p => p.SaleDate.Year == today.Year && p.SaleDate.Month == today.Month);
                }
            }

            if (startDate.HasValue)
            {
                var start = startDate.Value.Date;
                query = query.Where(p => p.SaleDate.Date >= start);
            }

            if (endDate.HasValue)
            {
                var end = endDate.Value.Date;
                query = query.Where(p => p.SaleDate.Date <= end);
            }

            return query.OrderByDescending(p => p.SaleDate).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<PosTransaction> RecordPosSaleAsync(long saleId, DateTime saleDate, decimal grossAmount, int? bankAccountId = null)
    {
        if (grossAmount <= 0)
        {
            throw new ArgumentException("Brüt satış tutarı 0'dan büyük olmalıdır.", nameof(grossAmount));
        }

        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            BankAccount? targetAccount = null;

            if (bankAccountId.HasValue)
            {
                targetAccount = accounts.FirstOrDefault(a => a.Id == bankAccountId.Value);
            }

            targetAccount ??= accounts.FirstOrDefault(a => a.BankName.Contains("Ziraat", StringComparison.OrdinalIgnoreCase))
                              ?? accounts.FirstOrDefault(a => a.IsDefault)
                              ?? accounts.FirstOrDefault(a => a.AccountName.Contains("POS", StringComparison.OrdinalIgnoreCase))
                              ?? accounts.FirstOrDefault();

            decimal commissionRate = targetAccount?.PosCommissionRate ?? 3.99m;
            int valourDays = targetAccount?.PosValourDays ?? 2;

            decimal commissionAmount = Math.Round(grossAmount * commissionRate / 100m, 2);
            decimal netAmount = grossAmount - commissionAmount;
            DateTime expectedDate = saleDate.AddDays(valourDays);

            var posList = await LoadPosTransactionsInternalAsync();

            // Mükerrer POS kaydı engeli
            var existing = posList.FirstOrDefault(p =>
                (p.SaleId.HasValue && p.SaleId.Value == saleId) ||
                (p.GrossAmount == grossAmount && Math.Abs((p.SaleDate - saleDate).TotalSeconds) < 5 && p.Status == "Bekliyor"));

            if (existing != null)
            {
                return existing;
            }

            var posItem = new PosTransaction
            {
                Id = posList.Count > 0 ? posList.Max(p => p.Id) + 1 : 1,
                SaleId = saleId,
                SaleDate = saleDate,
                GrossAmount = grossAmount,
                CommissionRate = commissionRate,
                CommissionAmount = commissionAmount,
                NetAmount = netAmount,
                ExpectedDate = expectedDate,
                BankAccountId = targetAccount?.Id,
                BankAccountName = targetAccount != null ? $"{targetAccount.BankName} - {targetAccount.AccountName}" : "Varsayılan POS",
                Status = "Bekliyor",
                Description = $"Satış/Fiş #{saleId} Kredi Kartı POS Tahsilatı"
            };

            posList.Insert(0, posItem);

            // Veritabanına doğrudan tekil atomik ekleme (SQL düzeyinde çift kayıt engelli)
            try
            {
                if (await _dbService.TestConnectionAsync())
                {
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();

                    // SQL Seviyesinde Mükerrer Kontrolü (Aynı SaleId veya son 15 saniyede aynı tutar varsa engelle)
                    var checkSql = @"
                        SELECT COUNT(*) FROM PosTransactions 
                        WHERE (SaleId = @sid AND @sid > 0) 
                           OR (GrossAmount = @gross AND Status = 'Bekliyor' AND SaleDate >= @minDate AND SaleDate <= @maxDate);";
                    await using (var checkCmd = conn.CreateCommand(_dbService.NormalizeSql(checkSql)))
                    {
                        checkCmd.AddParam("sid", posItem.SaleId ?? 0);
                        checkCmd.AddParam("gross", posItem.GrossAmount);
                        checkCmd.AddParam("minDate", posItem.SaleDate.AddSeconds(-15));
                        checkCmd.AddParam("maxDate", posItem.SaleDate.AddSeconds(15));
                        int dupCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                        if (dupCount > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[BankService] SQL düzeyinde mükerrer POS kaydı engellendi: SaleId {posItem.SaleId}, Tutar {posItem.GrossAmount}");
                            return posItem;
                        }
                    }

                    var insertSql = @"INSERT INTO PosTransactions (SaleId, SaleDate, GrossAmount, CommissionRate, CommissionAmount, NetAmount, ExpectedDate, ActualPaymentDate, ActualPaidAmount, BankAccountId, BankAccountName, Status, Description)
                                      VALUES (@sid, @sdate, @gross, @rate, @camt, @net, @expDate, @actDate, @actPaid, @baid, @baname, @stat, @desc);
                                      SELECT SCOPE_IDENTITY();";
                    await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(insertSql));
                    cmd.AddParam("sid", posItem.SaleId);
                    cmd.AddParam("sdate", posItem.SaleDate);
                    cmd.AddParam("gross", posItem.GrossAmount);
                    cmd.AddParam("rate", posItem.CommissionRate);
                    cmd.AddParam("camt", posItem.CommissionAmount);
                    cmd.AddParam("net", posItem.NetAmount);
                    cmd.AddParam("expDate", posItem.ExpectedDate);
                    cmd.AddParam("actDate", posItem.ActualPaymentDate);
                    cmd.AddParam("actPaid", posItem.ActualPaidAmount);
                    cmd.AddParam("baid", posItem.BankAccountId);
                    cmd.AddParam("baname", posItem.BankAccountName);
                    cmd.AddParam("stat", posItem.Status);
                    cmd.AddParam("desc", posItem.Description);
                    var scalar = await cmd.ExecuteScalarAsync();
                    if (scalar != null && scalar != DBNull.Value)
                    {
                        posItem.Id = Convert.ToInt64(scalar);
                    }
                }
            }
            catch { }

            try
            {
                EnsureStorageDirectory();
                var json = JsonSerializer.Serialize(posList, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(PosFile, json);
            }
            catch { }

            return posItem;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SettlePosTransactionAsync(
        long posTransactionId,
        int targetAccountId,
        decimal actualPaidAmount,
        DateTime paymentDate,
        string? description = null)
    {
        await EnsureInitializedAsync();

        PosTransaction? posItem;
        string accName = "Banka";

        await _semaphore.WaitAsync();
        try
        {
            var posList = await LoadPosTransactionsInternalAsync();
            posItem = posList.FirstOrDefault(p => p.Id == posTransactionId);
            if (posItem == null)
            {
                throw new InvalidOperationException("POS hareketi bulunamadı.");
            }

            var accounts = await LoadAccountsInternalAsync();
            var account = accounts.FirstOrDefault(a => a.Id == targetAccountId);
            if (account == null)
            {
                throw new InvalidOperationException("Hedef banka hesabı bulunamadı.");
            }

            accName = $"{account.BankName} - {account.AccountName}";

            posItem.Status = "Ödendi";
            posItem.ActualPaymentDate = paymentDate;
            posItem.ActualPaidAmount = actualPaidAmount;
            posItem.BankAccountId = targetAccountId;
            posItem.BankAccountName = accName;

            // Veritabanında doğrudan tekil anlık güncelleme (1500+ sorgu yerine 1 tekil sorgu)
            try
            {
                if (await _dbService.TestConnectionAsync())
                {
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();
                    var updateSql = @"UPDATE PosTransactions 
                                      SET Status = @stat, 
                                          ActualPaymentDate = @actDate, 
                                          ActualPaidAmount = @actPaid, 
                                          BankAccountId = @baid, 
                                          BankAccountName = @baname 
                                      WHERE Id = @id";
                    await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(updateSql));
                    cmd.AddParam("id", posItem.Id);
                    cmd.AddParam("stat", "Ödendi");
                    cmd.AddParam("actDate", paymentDate);
                    cmd.AddParam("actPaid", actualPaidAmount);
                    cmd.AddParam("baid", targetAccountId);
                    cmd.AddParam("baname", accName);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BankService] SettlePosTransactionAsync SQL hatası: {ex.Message}");
            }

            try
            {
                EnsureStorageDirectory();
                var json = JsonSerializer.Serialize(posList, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(PosFile, json);
            }
            catch { }
        }
        finally
        {
            _semaphore.Release();
        }

        // Banka hesabına giriş hareketi ekle
        var desc = string.IsNullOrWhiteSpace(description)
            ? $"POS Tahsilatı - Satış #{posItem.SaleId} (Brüt: ₺{posItem.GrossAmount:N2}, Komisyon: ₺{posItem.CommissionAmount:N2})"
            : description.Trim();

        await AddTransactionAsync(
            targetAccountId,
            "POS Tahsilatı",
            "Giriş",
            actualPaidAmount,
            desc,
            "Kredi Kartı / POS",
            posItem.Id,
            "POS",
            paymentDate);

        FinancialSyncBus.Notify(FinancialChangeType.PosSettlement, posItem);
    }

    public async Task SettleBatchPosTransactionsAsync(
        IEnumerable<long> posTransactionIds,
        int targetAccountId,
        decimal totalPaidAmount,
        DateTime paymentDate,
        string? description = null)
    {
        await EnsureInitializedAsync();

        var idSet = posTransactionIds.ToHashSet();
        if (idSet.Count == 0) return;

        List<PosTransaction> settledItems = new();
        string accName = "Banka";

        await _semaphore.WaitAsync();
        try
        {
            var posList = await LoadPosTransactionsInternalAsync();
            var accounts = await LoadAccountsInternalAsync();
            var account = accounts.FirstOrDefault(a => a.Id == targetAccountId);
            if (account == null)
            {
                throw new InvalidOperationException("Hedef banka hesabı bulunamadı.");
            }

            accName = $"{account.BankName} - {account.AccountName}";

            foreach (var pos in posList.Where(p => idSet.Contains(p.Id) && p.Status != "Ödendi"))
            {
                pos.Status = "Ödendi";
                pos.ActualPaymentDate = paymentDate;
                pos.ActualPaidAmount = pos.NetAmount;
                pos.BankAccountId = targetAccountId;
                pos.BankAccountName = accName;
                settledItems.Add(pos);
            }

            // Veritabanında tek seferde toplu güncelleme (1500+ sorgu yerine 1 tekil toplu sorgu)
            try
            {
                if (await _dbService.TestConnectionAsync())
                {
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();
                    var idsCsv = string.Join(",", settledItems.Select(p => p.Id));
                    var updateSql = $@"UPDATE PosTransactions 
                                      SET Status = @stat, 
                                          ActualPaymentDate = @actDate, 
                                          ActualPaidAmount = NetAmount, 
                                          BankAccountId = @baid, 
                                          BankAccountName = @baname 
                                      WHERE Id IN ({idsCsv})";
                    await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(updateSql));
                    cmd.AddParam("stat", "Ödendi");
                    cmd.AddParam("actDate", paymentDate);
                    cmd.AddParam("baid", targetAccountId);
                    cmd.AddParam("baname", accName);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BankService] SettleBatchPosTransactionsAsync SQL hatası: {ex.Message}");
            }

            try
            {
                EnsureStorageDirectory();
                var json = JsonSerializer.Serialize(posList, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(PosFile, json);
            }
            catch { }
        }
        finally
        {
            _semaphore.Release();
        }

        var desc = string.IsNullOrWhiteSpace(description)
            ? $"Toplu POS Tahsilatı ({settledItems.Count} Fiş - Toplam Brüt: ₺{settledItems.Sum(p => p.GrossAmount):N2}, Komisyon: ₺{settledItems.Sum(p => p.CommissionAmount):N2})"
            : description.Trim();

        await AddTransactionAsync(
            targetAccountId,
            "POS Tahsilatı (Toplu)",
            "Giriş",
            totalPaidAmount,
            desc,
            "Kredi Kartı / POS",
            null,
            "POS_BATCH",
            paymentDate);
    }

    public async Task UnsettlePosTransactionAsync(long posTransactionId)
    {
        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var posList = await LoadPosTransactionsInternalAsync();
            var posItem = posList.FirstOrDefault(p => p.Id == posTransactionId);
            if (posItem == null)
            {
                throw new InvalidOperationException("POS hareketi bulunamadı.");
            }

            if (posItem.Status != "Ödendi")
            {
                throw new InvalidOperationException("Yalnızca 'Ödendi' durumundaki tahsilatlar geri alınabilir.");
            }

            decimal paidAmount = posItem.ActualPaidAmount ?? posItem.NetAmount;
            int? bankAccountId = posItem.BankAccountId;

            posItem.Status = "Bekliyor";
            posItem.ActualPaymentDate = null;
            posItem.ActualPaidAmount = null;

            // 1. Veritabanında POS kaydını 'Bekliyor' durumuna geri al
            try
            {
                if (await _dbService.TestConnectionAsync())
                {
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();
                    var updateSql = @"UPDATE PosTransactions 
                                      SET Status = 'Bekliyor', 
                                          ActualPaymentDate = NULL, 
                                          ActualPaidAmount = NULL 
                                      WHERE Id = @id";
                    await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(updateSql));
                    cmd.AddParam("id", posItem.Id);
                    await cmd.ExecuteNonQueryAsync();

                    // 2. Bu tahsilata ait BankTransactions kaydı varsa sil
                    var delTxSql = @"DELETE FROM BankTransactions 
                                     WHERE (ReferenceType = 'POS' AND ReferenceId = @refId)";
                    await using var cmdDel = conn.CreateCommand(_dbService.NormalizeSql(delTxSql));
                    cmdDel.AddParam("refId", posItem.Id);
                    await cmdDel.ExecuteNonQueryAsync();

                    // 3. Banka hesabından tahsil edilen tutarı geri düş
                    if (bankAccountId.HasValue && bankAccountId.Value > 0)
                    {
                        var updateAccSql = @"UPDATE BankAccounts 
                                             SET CurrentBalance = CurrentBalance - @amount, 
                                                 ActualBalance = ActualBalance - @amount 
                                             WHERE Id = @accId";
                        await using var cmdAcc = conn.CreateCommand(_dbService.NormalizeSql(updateAccSql));
                        cmdAcc.AddParam("amount", paidAmount);
                        cmdAcc.AddParam("accId", bankAccountId.Value);
                        await cmdAcc.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BankService] UnsettlePosTransactionAsync SQL hatası: {ex.Message}");
            }

            // Yerel JSON dosyalarını güncelle
            try
            {
                EnsureStorageDirectory();
                var json = JsonSerializer.Serialize(posList, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(PosFile, json);

                if (bankAccountId.HasValue)
                {
                    var accounts = await LoadAccountsInternalAsync();
                    var acc = accounts.FirstOrDefault(a => a.Id == bankAccountId.Value);
                    if (acc != null)
                    {
                        acc.CurrentBalance -= paidAmount;
                        acc.ActualBalance -= paidAmount;
                        var jsonAcc = JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(AccountsFile, jsonAcc);
                    }
                }

                var transactions = await LoadTransactionsInternalAsync();
                var txToRemove = transactions.FirstOrDefault(t => t.ReferenceType == "POS" && t.ReferenceId == posItem.Id);
                if (txToRemove != null)
                {
                    transactions.Remove(txToRemove);
                    var jsonTx = JsonSerializer.Serialize(transactions, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(TransactionsFile, jsonTx);
                }
            }
            catch { }
            FinancialSyncBus.Notify(FinancialChangeType.PosSettlement, posItem);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    #endregion

    #region Cash - Bank Transfer

    public async Task TransferCashToBankAsync(int targetBankAccountId, decimal amount, DateTime date, string description)
    {
        if (amount <= 0) throw new ArgumentException("Transfer tutarı 0'dan büyük olmalıdır.");

        var account = await GetAccountByIdAsync(targetBankAccountId);
        if (account == null) throw new InvalidOperationException("Hedef banka hesabı bulunamadı.");

        var desc = string.IsNullOrWhiteSpace(description) ? "Kasadan bankaya nakit aktarımı" : description.Trim();

        // 1. Bankaya para girişi
        await AddTransactionAsync(
            targetBankAccountId,
            "Kasa Transferi",
            "Giriş",
            amount,
            $"Kasadan banka hesabına transfer. {desc}",
            "Nakit",
            null,
            "KASA",
            date);

        // 2. Kasadan para çıkışı (CashService)
        try
        {
            var cashService = new CashService(_dbService);
            await cashService.AddTransactionAsync(
                "Bankaya Transfer",
                amount,
                $"Banka hesabına transfer ({account.BankName} - {account.AccountName}). {desc}");
        }
        catch { }
    }

    public async Task TransferBankToCashAsync(int sourceBankAccountId, decimal amount, DateTime date, string description)
    {
        if (amount <= 0) throw new ArgumentException("Transfer tutarı 0'dan büyük olmalıdır.");

        var account = await GetAccountByIdAsync(sourceBankAccountId);
        if (account == null) throw new InvalidOperationException("Kaynak banka hesabı bulunamadı.");

        var desc = string.IsNullOrWhiteSpace(description) ? "Bankadan kasaya nakit çekimi" : description.Trim();

        // 1. Bankadan para çıkışı
        await AddTransactionAsync(
            sourceBankAccountId,
            "Kasa Transferi",
            "Çıkış",
            amount,
            $"Bankadan kasaya nakit çekme. {desc}",
            "Nakit",
            null,
            "KASA",
            date);

        // 2. Kasaya para girişi (CashService)
        try
        {
            var cashService = new CashService(_dbService);
            await cashService.AddTransactionAsync(
                "Bankadan Transfer",
                amount,
                $"Banka hesabından çekilen nakit ({account.BankName} - {account.AccountName}). {desc}");
        }
        catch { }
    }

    #endregion

    #region Reconciliation (Mutabakat)

    public async Task UpdateActualBalanceAsync(int accountId, decimal actualBalance)
    {
        await EnsureInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            var account = accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                account.ActualBalance = actualBalance;
                await SaveAccountsInternalAsync(accounts);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ReconcileAccountAsync(int accountId, decimal actualBalance, bool createAdjustment = false, string? note = null)
    {
        await UpdateActualBalanceAsync(accountId, actualBalance);

        if (createAdjustment)
        {
            var account = await GetAccountByIdAsync(accountId);
            if (account != null)
            {
                var diff = actualBalance - account.CurrentBalance;
                if (diff != 0)
                {
                    string dir = diff > 0 ? "Giriş" : "Çıkış";
                    decimal absDiff = Math.Abs(diff);
                    string desc = $"Mutabakat Bakiye Düzeltmesi (Fark: {(diff > 0 ? "+" : "")}₺{diff:N2}). {note}".Trim();

                    await AddTransactionAsync(
                        accountId,
                        "Mutabakat Düzeltmesi",
                        dir,
                        absDiff,
                        desc,
                        "Diğer",
                        null,
                        "MANUEL");
                }
            }
        }
    }

    #endregion

    #region Summary

    public BankSummary CalculateSummary(List<BankAccount> accounts, List<BankTransaction> transactions, List<PosTransaction> posList, DateTime? targetDate = null)
    {
        var date = (targetDate ?? DateTime.Today).Date;
        var todayTxs = transactions.Where(t => t.CreatedAt.Date == date).ToList();

        decimal totalBankBalance = accounts.Sum(a => a.CurrentBalance);
        decimal todayInflow = todayTxs.Sum(t => t.Inflow);
        decimal todayOutflow = todayTxs.Sum(t => t.Outflow);

        var pendingPos = posList.Where(p => p.Status != "Ödendi").ToList();
        decimal pendingGrossPos = pendingPos.Sum(p => p.GrossAmount);
        decimal pendingNetPos = pendingPos.Sum(p => p.NetAmount);
        decimal pendingCommission = pendingPos.Sum(p => p.CommissionAmount);

        // Bugün yapılan POS Satışları
        var todaySoldPos = posList.Where(p => p.SaleDate.Date == date).ToList();
        decimal todayPosSalesGross = todaySoldPos.Sum(p => p.GrossAmount);
        decimal todayPosSalesCommission = todaySoldPos.Sum(p => p.CommissionAmount);
        decimal todayPosSalesNet = todaySoldPos.Sum(p => p.NetAmount);

        // Bugün vadesi dolan (tahsil edilmesi beklenen) POS alacakları
        var todayDuePos = pendingPos.Where(p => p.ExpectedDate.Date == date).ToList();
        decimal todayDueGross = todayDuePos.Sum(p => p.GrossAmount);
        decimal todayDueNet = todayDuePos.Sum(p => p.NetAmount);
        decimal todayDueComm = todayDuePos.Sum(p => p.CommissionAmount);

        decimal todayPosInflow = todayTxs.Where(t => t.TransactionType == "POS Tahsilatı").Sum(t => t.Inflow);
        decimal todayExtInflow = todayTxs.Where(t => t.TransactionType.Contains("Dışarıdan") || t.TransactionType.Contains("Sermaye") || t.TransactionType == "Para Girişi").Sum(t => t.Inflow);

        decimal openingBalance = totalBankBalance - todayInflow + todayOutflow;

        return new BankSummary
        {
            TotalBankBalance = totalBankBalance,
            TodayInflow = todayInflow,
            TodayOutflow = todayOutflow,
            PendingGrossPos = pendingGrossPos,
            PendingNetPos = pendingNetPos,
            PendingPosCommission = pendingCommission,
            TodayPosSalesGross = todayPosSalesGross,
            TodayPosSalesNet = todayPosSalesNet,
            TodayPosSalesCommission = todayPosSalesCommission,
            TodayPosSalesCount = todaySoldPos.Count,
            TodayDuePosGross = todayDueGross,
            TodayDuePosNet = todayDueNet,
            TodayDuePosCommission = todayDueComm,
            TodayDuePosCount = todayDuePos.Count,
            TodayPosInflow = todayPosInflow,
            TodayExternalInflow = todayExtInflow,
            OpeningTotalBalance = openingBalance
        };
    }

    public async Task<BankSummary> GetBankSummaryAsync(DateTime? targetDate = null)
    {
        await EnsureInitializedAsync();

        var accounts = await GetAllAccountsAsync();
        var transactions = await GetTransactionsAsync();
        var posList = await GetPosTransactionsAsync();

        return CalculateSummary(accounts, transactions, posList, targetDate);
    }

    #endregion

    #region Internal Load / Save / Seed

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        EnsureStorageDirectory();

        // 1. Hesapları öncelikle doğrudan veritabanından çek
        if (await _dbService.TestConnectionAsync())
        {
            await LoadAccountsInternalAsync();
        }
        else if (!File.Exists(AccountsFile))
        {
            var initialAccounts = new List<BankAccount>
            {
                new BankAccount
                {
                    Id = 1,
                    BankName = "Ana Ticari Banka",
                    AccountName = "Vadesiz Ticari Hesap",
                    Iban = "",
                    AccountNumber = "",
                    Branch = "Merkez",
                    Currency = "TRY",
                    OpeningBalance = 0m,
                    CurrentBalance = 0m,
                    ActualBalance = 0m,
                    PosCommissionRate = 0m,
                    PosValourDays = 1,
                    IsDefault = true,
                    IsActive = true
                }
            };

            var json = JsonSerializer.Serialize(initialAccounts, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(AccountsFile, json);
        }

        // 2. Hareketler dosyası yoksa boş liste oluştur
        if (!File.Exists(TransactionsFile))
        {
            var initialTxs = new List<BankTransaction>
            {
                new BankTransaction
                {
                    Id = 1,
                    CreatedAt = DateTime.Today.AddDays(-5),
                    BankAccountId = 1,
                    BankAccountName = "Ziraat Bankası - Ana Ticari Hesap",
                    TransactionType = "Açılış Bakiyesi",
                    Direction = "Giriş",
                    Inflow = 7894.13m,
                    Outflow = 0,
                    BalanceAfter = 7894.13m,
                    Description = "Sistem Başlangıç Açılış Bakiyesi",
                    PaymentMethod = "Diğer",
                    ReferenceType = "MANUEL"
                },
                new BankTransaction
                {
                    Id = 2,
                    CreatedAt = DateTime.Today.AddDays(-5),
                    BankAccountId = 2,
                    BankAccountName = "İş Bankası - POS Tahsilat Hesabı",
                    TransactionType = "Açılış Bakiyesi",
                    Direction = "Giriş",
                    Inflow = 50450.00m,
                    Outflow = 0,
                    BalanceAfter = 50450.00m,
                    Description = "Sistem Başlangıç Açılış Bakiyesi",
                    PaymentMethod = "Diğer",
                    ReferenceType = "MANUEL"
                }
            };
            var json = JsonSerializer.Serialize(initialTxs, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(TransactionsFile, json);
        }

        // 3. POS hareketleri dosyası yoksa veya geçmiş satışlar varsa senkronize et
        await SyncHistoricalCreditCardSalesAsync();
    }

    private async Task SyncHistoricalCreditCardSalesAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                var sql = @"
                    SELECT s.Id, s.SaleDate, s.PaymentType, s.GrandTotal, s.CardAmount 
                    FROM Sales s
                    LEFT JOIN PosTransactions p ON s.Id = p.SaleId
                    WHERE (s.PaymentType = 'Kredi Kartı' OR s.CardAmount > 0)
                      AND p.Id IS NULL
                    ORDER BY s.Id ASC;";
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();

                var missingFromDb = new List<(long Id, DateTime Date, decimal Gross)>();
                while (await reader.ReadAsync())
                {
                    long saleId = Convert.ToInt64(reader[0]);
                    DateTime saleDate = Convert.ToDateTime(reader[1]);
                    decimal grandTotal = Convert.ToDecimal(reader[3]);
                    decimal cardAmt = Convert.ToDecimal(reader[4]);
                    decimal gross = cardAmt > 0 ? cardAmt : grandTotal;
                    if (gross > 0)
                    {
                        missingFromDb.Add((saleId, saleDate, gross));
                    }
                }
                await reader.CloseAsync();

                if (missingFromDb.Count > 0)
                {
                    var accounts = await LoadAccountsInternalAsync();
                    var targetAccount = accounts.FirstOrDefault(a => a.BankName.Contains("Ziraat", StringComparison.OrdinalIgnoreCase))
                                      ?? accounts.FirstOrDefault(a => a.IsDefault)
                                      ?? accounts.FirstOrDefault(a => a.AccountName.Contains("POS", StringComparison.OrdinalIgnoreCase))
                                      ?? accounts.FirstOrDefault();
                    int targetAccountId = targetAccount?.Id ?? 1;

                    foreach (var item in missingFromDb)
                    {
                        await RecordPosSaleAsync(item.Id, item.Date, item.Gross, targetAccountId);
                    }
                }
            }
        }
        catch { }

        try
        {
            var posList = new List<PosTransaction>();
            if (File.Exists(PosFile))
            {
                var existingJson = await File.ReadAllTextAsync(PosFile);
                posList = JsonSerializer.Deserialize<List<PosTransaction>>(existingJson) ?? new List<PosTransaction>();
            }

            // Sales.json'dan kredi kartı satışlarını tara
            var salesFile = Path.Combine(StorageDirectory, "sales.json");
            if (File.Exists(salesFile))
            {
                var salesJson = await File.ReadAllTextAsync(salesFile);
                var sales = JsonSerializer.Deserialize<List<Sale>>(salesJson) ?? new List<Sale>();

                bool modified = false;
                foreach (var s in sales.Where(x => x.PaymentType == "Kredi Kartı" || x.CardAmount > 0))
                {
                    if (!posList.Any(p => p.SaleId == s.Id))
                    {
                        decimal gross = s.CardAmount > 0 ? s.CardAmount : s.GrandTotal;
                        decimal rate = 3.99m;
                        decimal comm = Math.Round(gross * rate / 100m, 2);
                        decimal net = gross - comm;

                        posList.Add(new PosTransaction
                        {
                            Id = posList.Count > 0 ? posList.Max(p => p.Id) + 1 : 1,
                            SaleId = s.Id,
                            SaleDate = s.SaleDate,
                            GrossAmount = gross,
                            CommissionRate = rate,
                            CommissionAmount = comm,
                            NetAmount = net,
                            ExpectedDate = s.SaleDate.AddDays(2),
                            BankAccountId = 1,
                            BankAccountName = "Ziraat Bankası - Ana Ticari Hesap",
                            Status = "Bekliyor",
                            Description = $"Fiş #{s.Id} Kredi Kartı Satışı"
                        });
                        modified = true;
                    }
                }

                if (modified || !File.Exists(PosFile))
                {
                    var json = JsonSerializer.Serialize(posList, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(PosFile, json);
                }
            }
        }
        catch { }
    }

    private async Task<List<BankAccount>> LoadAccountsInternalAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<BankAccount>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = @"SELECT Id, BankName, AccountName, Iban, AccountNumber, Branch, Currency, OpeningBalance, CurrentBalance, 
                                   ActualBalance, PosCommissionRate, PosValourDays, IsDefault, IsActive, CreatedAt,
                                   AccountType, CreditLimit, AvailableLimit, CurrentDebt, CutoffDay, DueDay, CardNumberLast4 
                            FROM BankAccounts";
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new BankAccount
                    {
                        Id = reader.GetInt32(0),
                        BankName = reader.GetString(1),
                        AccountName = reader.GetString(2),
                        Iban = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        AccountNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        Branch = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        Currency = reader.GetString(6),
                        OpeningBalance = reader.GetDecimal(7),
                        CurrentBalance = reader.GetDecimal(8),
                        ActualBalance = reader.GetDecimal(9),
                        PosCommissionRate = reader.GetDecimal(10),
                        PosValourDays = reader.GetInt32(11),
                        IsDefault = reader.GetBoolean(12),
                        IsActive = reader.GetBoolean(13),
                        CreatedAt = reader.GetDateTime(14),
                        AccountType = reader.IsDBNull(15) ? "Vadesiz Mevduat" : reader.GetString(15),
                        CreditLimit = reader.IsDBNull(16) ? 0m : reader.GetDecimal(16),
                        AvailableLimit = reader.IsDBNull(17) ? 0m : reader.GetDecimal(17),
                        CurrentDebt = reader.IsDBNull(18) ? 0m : reader.GetDecimal(18),
                        CutoffDay = reader.IsDBNull(19) ? null : reader.GetInt32(19),
                        DueDay = reader.IsDBNull(20) ? null : reader.GetInt32(20),
                        CardNumberLast4 = reader.IsDBNull(21) ? null : reader.GetString(21)
                    });
                }
                if (list.Count > 0)
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(AccountsFile, json);
                        AppDataCache.UpdateBankAccounts(list);
                    }
                    catch { }
                    return list;
                }
            }
        }
        catch { }

        try
        {
            if (File.Exists(AccountsFile))
            {
                var json = await File.ReadAllTextAsync(AccountsFile);
                return JsonSerializer.Deserialize<List<BankAccount>>(json) ?? new List<BankAccount>();
            }
        }
        catch { }
        return new List<BankAccount>();
    }

    private async Task SaveAccountsInternalAsync(List<BankAccount> accounts)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var b in accounts)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM BankAccounts WHERE Id = @id");
                    checkCmd.AddParam("id", b.Id);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (exists)
                    {
                        var updateSql = @"
                            UPDATE BankAccounts 
                            SET BankName=@bn, AccountName=@an, Iban=@iban, AccountNumber=@accNo, Branch=@br, Currency=@cur, 
                                OpeningBalance=@opBal, CurrentBalance=@curBal, ActualBalance=@actBal, PosCommissionRate=@rate, 
                                PosValourDays=@days, IsDefault=@def, IsActive=@act, AccountType=@atype, CreditLimit=@clim, 
                                AvailableLimit=@alim, CurrentDebt=@cdebt, CutoffDay=@cday, DueDay=@dday, CardNumberLast4=@last4 
                            WHERE Id=@id";
                        await using var cmd = conn.CreateCommand(updateSql);
                        cmd.AddParam("id", b.Id);
                        cmd.AddParam("bn", b.BankName);
                        cmd.AddParam("an", b.AccountName);
                        cmd.AddParam("iban", b.Iban);
                        cmd.AddParam("accNo", b.AccountNumber);
                        cmd.AddParam("br", b.Branch);
                        cmd.AddParam("cur", b.Currency);
                        cmd.AddParam("opBal", b.OpeningBalance);
                        cmd.AddParam("curBal", b.CurrentBalance);
                        cmd.AddParam("actBal", b.ActualBalance);
                        cmd.AddParam("rate", b.PosCommissionRate);
                        cmd.AddParam("days", b.PosValourDays);
                        cmd.AddParam("def", b.IsDefault);
                        cmd.AddParam("act", b.IsActive);
                        cmd.AddParam("atype", b.AccountType);
                        cmd.AddParam("clim", b.CreditLimit);
                        cmd.AddParam("alim", b.AvailableLimit);
                        cmd.AddParam("cdebt", b.CurrentDebt);
                        cmd.AddParam("cday", b.CutoffDay);
                        cmd.AddParam("dday", b.DueDay);
                        cmd.AddParam("last4", b.CardNumberLast4);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var insertSql = @"
                            INSERT INTO BankAccounts (Id, BankName, AccountName, Iban, AccountNumber, Branch, Currency, 
                                                      OpeningBalance, CurrentBalance, ActualBalance, PosCommissionRate, 
                                                      PosValourDays, IsDefault, IsActive, CreatedAt, AccountType, CreditLimit, 
                                                      AvailableLimit, CurrentDebt, CutoffDay, DueDay, CardNumberLast4)
                            VALUES (@id, @bn, @an, @iban, @accNo, @br, @cur, @opBal, @curBal, @actBal, @rate, @days, 
                                    @def, @act, @created, @atype, @clim, @alim, @cdebt, @cday, @dday, @last4);";
                        await using var cmd = conn.CreateCommand(insertSql);
                        cmd.AddParam("id", b.Id);
                        cmd.AddParam("bn", b.BankName);
                        cmd.AddParam("an", b.AccountName);
                        cmd.AddParam("iban", b.Iban);
                        cmd.AddParam("accNo", b.AccountNumber);
                        cmd.AddParam("br", b.Branch);
                        cmd.AddParam("cur", b.Currency);
                        cmd.AddParam("opBal", b.OpeningBalance);
                        cmd.AddParam("curBal", b.CurrentBalance);
                        cmd.AddParam("actBal", b.ActualBalance);
                        cmd.AddParam("rate", b.PosCommissionRate);
                        cmd.AddParam("days", b.PosValourDays);
                        cmd.AddParam("def", b.IsDefault);
                        cmd.AddParam("act", b.IsActive);
                        cmd.AddParam("created", b.CreatedAt);
                        cmd.AddParam("atype", b.AccountType);
                        cmd.AddParam("clim", b.CreditLimit);
                        cmd.AddParam("alim", b.AvailableLimit);
                        cmd.AddParam("cdebt", b.CurrentDebt);
                        cmd.AddParam("cday", b.CutoffDay);
                        cmd.AddParam("dday", b.DueDay);
                        cmd.AddParam("last4", b.CardNumberLast4);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
        catch { }

        EnsureStorageDirectory();
        var json = JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(AccountsFile, json);
    }

    private async Task<List<BankTransaction>> LoadTransactionsInternalAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<BankTransaction>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = "SELECT Id, CreatedAt, BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, ReferenceId, ReferenceType, \"User\" FROM BankTransactions ORDER BY CreatedAt DESC";
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new BankTransaction
                    {
                        Id = reader.GetInt64(0),
                        CreatedAt = reader.GetDateTime(1),
                        BankAccountId = reader.GetInt32(2),
                        BankAccountName = reader.GetString(3),
                        TransactionType = reader.GetString(4),
                        Direction = reader.GetString(5),
                        Inflow = reader.GetDecimal(6),
                        Outflow = reader.GetDecimal(7),
                        BalanceAfter = reader.GetDecimal(8),
                        Description = reader.IsDBNull(9) ? "" : reader.GetString(9),
                        PaymentMethod = reader.GetString(10),
                        ReferenceId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
                        ReferenceType = reader.IsDBNull(12) ? null : reader.GetString(12),
                        User = reader.GetString(13)
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
                return JsonSerializer.Deserialize<List<BankTransaction>>(json) ?? new List<BankTransaction>();
            }
        }
        catch { }
        return new List<BankTransaction>();
    }

    private async Task SaveTransactionsInternalAsync(List<BankTransaction> transactions)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var t in transactions)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM BankTransactions WHERE Id = @id");
                    checkCmd.AddParam("id", t.Id);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (!exists)
                    {
                        var insertSql = @"
                            INSERT INTO BankTransactions (Id, CreatedAt, BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, ReferenceId, ReferenceType, ""User"")
                            VALUES (@id, @created, @baid, @baname, @type, @dir, @inflow, @outflow, @bal, @desc, @pm, @refId, @refType, @user);";
                        await using var cmd = conn.CreateCommand(insertSql);
                        cmd.AddParam("id", t.Id);
                        cmd.AddParam("created", t.CreatedAt);
                        cmd.AddParam("baid", t.BankAccountId);
                        cmd.AddParam("baname", t.BankAccountName);
                        cmd.AddParam("type", t.TransactionType);
                        cmd.AddParam("dir", t.Direction);
                        cmd.AddParam("inflow", t.Inflow);
                        cmd.AddParam("outflow", t.Outflow);
                        cmd.AddParam("bal", t.BalanceAfter);
                        cmd.AddParam("desc", t.Description);
                        cmd.AddParam("pm", t.PaymentMethod);
                        cmd.AddParam("refId", t.ReferenceId);
                        cmd.AddParam("refType", t.ReferenceType);
                        cmd.AddParam("user", t.User);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
        catch { }

        EnsureStorageDirectory();
        var json = JsonSerializer.Serialize(transactions, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(TransactionsFile, json);
    }


    private async Task<List<PosTransaction>> LoadPosTransactionsInternalAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<PosTransaction>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = "SELECT Id, SaleId, SaleDate, GrossAmount, CommissionRate, CommissionAmount, NetAmount, ExpectedDate, ActualPaymentDate, ActualPaidAmount, BankAccountId, BankAccountName, Status, Description FROM PosTransactions ORDER BY SaleDate DESC";
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new PosTransaction
                    {
                        Id = reader.GetInt64(0),
                        SaleId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                        SaleDate = reader.GetDateTime(2),
                        GrossAmount = reader.GetDecimal(3),
                        CommissionRate = reader.GetDecimal(4),
                        CommissionAmount = reader.GetDecimal(5),
                        NetAmount = reader.GetDecimal(6),
                        ExpectedDate = reader.GetDateTime(7),
                        ActualPaymentDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                        ActualPaidAmount = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                        BankAccountId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                        BankAccountName = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Status = reader.GetString(12),
                        Description = reader.IsDBNull(13) ? "" : reader.GetString(13)
                    });
                }
                if (list.Count > 0)
                {
                    try
                    {
                        EnsureStorageDirectory();
                        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                        _ = Task.Run(async () =>
                        {
                            try { await File.WriteAllTextAsync(PosFile, json); } catch { }
                        });
                    }
                    catch { }
                    return list;
                }
            }
        }
        catch { }

        try
        {
            if (File.Exists(PosFile))
            {
                var json = await File.ReadAllTextAsync(PosFile);
                return JsonSerializer.Deserialize<List<PosTransaction>>(json) ?? new List<PosTransaction>();
            }
        }
        catch { }
        return new List<PosTransaction>();
    }

    private async Task SavePosTransactionsInternalAsync(List<PosTransaction> posTransactions)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var p in posTransactions)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM PosTransactions WHERE Id = @id");
                    checkCmd.AddParam("id", p.Id);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (exists)
                    {
                        var updateSql = @"UPDATE PosTransactions SET Status=@stat, ActualPaymentDate=@actDate, ActualPaidAmount=@actPaid, CommissionRate=@rate, CommissionAmount=@camt, NetAmount=@net, ExpectedDate=@expDate, BankAccountId=@baid, BankAccountName=@baname, Description=@desc WHERE Id=@id";
                        await using var cmd = conn.CreateCommand(updateSql);
                        cmd.AddParam("id", p.Id);
                        cmd.AddParam("stat", p.Status);
                        cmd.AddParam("actDate", p.ActualPaymentDate);
                        cmd.AddParam("actPaid", p.ActualPaidAmount);
                        cmd.AddParam("rate", p.CommissionRate);
                        cmd.AddParam("camt", p.CommissionAmount);
                        cmd.AddParam("net", p.NetAmount);
                        cmd.AddParam("expDate", p.ExpectedDate);
                        cmd.AddParam("baid", p.BankAccountId);
                        cmd.AddParam("baname", p.BankAccountName);
                        cmd.AddParam("desc", p.Description);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var insertSql = @"INSERT INTO PosTransactions (Id, SaleId, SaleDate, GrossAmount, CommissionRate, CommissionAmount, NetAmount, ExpectedDate, ActualPaymentDate, ActualPaidAmount, BankAccountId, BankAccountName, Status, Description)
                                          VALUES (@id, @sid, @sdate, @gross, @rate, @camt, @net, @expDate, @actDate, @actPaid, @baid, @baname, @stat, @desc);";
                        await using var cmd = conn.CreateCommand(insertSql);
                        cmd.AddParam("id", p.Id);
                        cmd.AddParam("sid", p.SaleId);
                        cmd.AddParam("sdate", p.SaleDate);
                        cmd.AddParam("gross", p.GrossAmount);
                        cmd.AddParam("rate", p.CommissionRate);
                        cmd.AddParam("camt", p.CommissionAmount);
                        cmd.AddParam("net", p.NetAmount);
                        cmd.AddParam("expDate", p.ExpectedDate);
                        cmd.AddParam("actDate", p.ActualPaymentDate);
                        cmd.AddParam("actPaid", p.ActualPaidAmount);
                        cmd.AddParam("baid", p.BankAccountId);
                        cmd.AddParam("baname", p.BankAccountName);
                        cmd.AddParam("stat", p.Status);
                        cmd.AddParam("desc", p.Description);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
        catch { }

        EnsureStorageDirectory();
        var json = JsonSerializer.Serialize(posTransactions, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(PosFile, json);
    }

    #endregion
}
