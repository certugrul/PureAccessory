using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class CariService
{
    private readonly DatabaseService _dbService;
    private readonly FinancialTransactionCoordinator _coordinator;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string AccountsFile = Path.Combine(StorageDirectory, "cari_accounts.json");
    private static readonly string TransactionsFile = Path.Combine(StorageDirectory, "cari_transactions.json");
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public CariService(DatabaseService? dbService = null, FinancialTransactionCoordinator? coordinator = null)
    {
        _dbService = dbService ?? new DatabaseService();
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

    public async Task<List<CariAccount>> GetAllAccountsAsync(bool includeInactive = false)
    {
        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            if (accounts.Count == 0)
            {
                // Varsayılan cariler ekle
                accounts = new List<CariAccount>
                {
                    new CariAccount { Id = 1, Code = "CAR-0001", Name = "Perakende Veresiye Müşterileri", Type = "Müşteri", Phone = "0555 000 0001", Balance = 0 },
                    new CariAccount { Id = 2, Code = "CAR-0002", Name = "Toptan Aksesuar San. Tic. Ltd.", Type = "Tedarikçi", Phone = "0212 555 1020", Balance = 0 },
                    new CariAccount { Id = 3, Code = "CAR-0003", Name = "Mega Elektronik Tedarik A.Ş.", Type = "Tedarikçi", Phone = "0216 444 2030", Balance = 0 }
                };
                await SaveAccountsInternalAsync(accounts);
            }

            return includeInactive ? accounts : accounts.Where(a => a.IsActive).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<CariAccount> SaveOrUpdateAccountAsync(CariAccount account)
    {
        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            if (account.Id == 0)
            {
                account.Id = accounts.Count > 0 ? accounts.Max(a => a.Id) + 1 : 1;
                if (string.IsNullOrWhiteSpace(account.Code))
                {
                    account.Code = $"CAR-{account.Id:D4}";
                }
                accounts.Add(account);
            }
            else
            {
                var idx = accounts.FindIndex(a => a.Id == account.Id);
                if (idx >= 0) accounts[idx] = account;
                else accounts.Add(account);
            }

            await SaveAccountsInternalAsync(accounts);
            return account;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteAccountAsync(int id)
    {
        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            var target = accounts.FirstOrDefault(a => a.Id == id);
            if (target != null)
            {
                target.IsActive = false; // Soft delete
                await SaveAccountsInternalAsync(accounts);
                return true;
            }
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<CariTransaction>> GetTransactionsAsync(
        int? accountId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? searchTerm = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadTransactionsInternalAsync();
            var query = list.AsQueryable();

            if (accountId.HasValue && accountId.Value > 0)
            {
                query = query.Where(t => t.CariAccountId == accountId.Value);
            }

            if (startDate.HasValue)
            {
                query = query.Where(t => t.CreatedAt.Date >= startDate.Value.Date);
            }

            if (endDate.HasValue)
            {
                query = query.Where(t => t.CreatedAt.Date <= endDate.Value.Date);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLowerInvariant();
                query = query.Where(t =>
                    t.CariAccountName.ToLowerInvariant().Contains(term) ||
                    t.DocumentNo.ToLowerInvariant().Contains(term) ||
                    t.Description.ToLowerInvariant().Contains(term) ||
                    t.TransactionType.ToLowerInvariant().Contains(term));
            }

            return query.OrderByDescending(t => t.CreatedAt).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<CariTransaction> AddTransactionAsync(
        int cariAccountId,
        string transactionType,
        decimal debit,
        decimal credit,
        string documentNo,
        string description,
        DateTime? customDate = null,
        string paymentMethod = "Açık Hesap",
        int? bankAccountId = null,
        long? invoiceId = null,
        long? saleId = null)
    {
        // 1. Veritabanına atomik ve ilişkili finans (Kasa/Banka) hareketiyle kaydet
        if (await _dbService.TestConnectionAsync())
        {
            var txResult = await _coordinator.ProcessCariTransactionAsync(
                cariAccountId,
                transactionType,
                debit,
                credit,
                documentNo,
                description,
                paymentMethod,
                bankAccountId,
                customDate);

            // Yerel JSON'a da yedekle
            await _semaphore.WaitAsync();
            try
            {
                var transactions = await LoadTransactionsInternalAsync();
                var accounts = await LoadAccountsInternalAsync();
                txResult.CariAccountName = accounts.FirstOrDefault(a => a.Id == cariAccountId)?.Name ?? $"Cari #{cariAccountId}";
                transactions.Insert(0, txResult);
                var json = JsonSerializer.Serialize(transactions, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(TransactionsFile, json);
            }
            catch { }
            finally
            {
                _semaphore.Release();
            }

            return txResult;
        }

        // Yerel JSON Fallback
        await _semaphore.WaitAsync();
        try
        {
            var accounts = await LoadAccountsInternalAsync();
            var account = accounts.FirstOrDefault(a => a.Id == cariAccountId);
            if (account == null)
            {
                throw new InvalidOperationException($"Cari hesap bulunamadı! (Id: {cariAccountId})");
            }

            // Bakiye güncelleme: Borç (+) artırır, Alacak (-) azaltır
            account.Balance += (debit - credit);

            var transactions = await LoadTransactionsInternalAsync();
            var tx = new CariTransaction
            {
                Id = transactions.Count > 0 ? transactions.Max(t => t.Id) + 1 : 1,
                CreatedAt = customDate ?? DateTime.Now,
                CariAccountId = account.Id,
                CariAccountName = account.Name,
                TransactionType = transactionType,
                DocumentNo = documentNo,
                Debit = debit,
                Credit = credit,
                BalanceAfter = account.Balance,
                Description = description,
                PaymentMethod = paymentMethod,
                InvoiceId = invoiceId,
                SaleId = saleId
            };

            transactions.Insert(0, tx);

            await SaveAccountsInternalAsync(accounts);
            await SaveTransactionsInternalAsync(transactions);

            return tx;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteTransactionAsync(long transactionId)
    {
        await _semaphore.WaitAsync();
        try
        {
            var transactions = await LoadTransactionsInternalAsync();
            var tx = transactions.FirstOrDefault(t => t.Id == transactionId);
            if (tx == null) return false;

            // Bakiyeyi tersine çevirerek düzelt
            var accounts = await LoadAccountsInternalAsync();
            var account = accounts.FirstOrDefault(a => a.Id == tx.CariAccountId);
            if (account != null)
            {
                account.Balance -= (tx.Debit - tx.Credit);
                await SaveAccountsInternalAsync(accounts);
            }

            transactions.Remove(tx);
            await SaveTransactionsInternalAsync(transactions);
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<List<CariAccount>> LoadAccountsInternalAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<CariAccount>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = "SELECT Id, Code, Name, Type, Phone, Email, Address, TaxNo, TaxOffice, Balance, IsActive, CreatedAt FROM CariAccounts";
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new CariAccount
                    {
                        Id = reader.GetInt32(0),
                        Code = reader.GetString(1),
                        Name = reader.GetString(2),
                        Type = reader.GetString(3),
                        Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Email = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Address = reader.IsDBNull(6) ? null : reader.GetString(6),
                        TaxNo = reader.IsDBNull(7) ? null : reader.GetString(7),
                        TaxOffice = reader.IsDBNull(8) ? null : reader.GetString(8),
                        Balance = reader.GetDecimal(9),
                        IsActive = reader.GetBoolean(10),
                        CreatedAt = reader.GetDateTime(11)
                    });
                }
                if (list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch { }

        if (!File.Exists(AccountsFile)) return new List<CariAccount>();
        try
        {
            var json = await File.ReadAllTextAsync(AccountsFile);
            return JsonSerializer.Deserialize<List<CariAccount>>(json) ?? new List<CariAccount>();
        }
        catch
        {
            return new List<CariAccount>();
        }
    }

    private async Task SaveAccountsInternalAsync(List<CariAccount> list)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var c in list)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM CariAccounts WHERE Id = @id");
                    checkCmd.AddParam("id", c.Id);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (exists)
                    {
                        var updateSql = @"UPDATE CariAccounts SET Code=@code, Name=@name, Type=@type, Phone=@phone, Email=@email, Address=@addr, TaxNo=@taxNo, TaxOffice=@taxOff, Balance=@bal, IsActive=@act WHERE Id=@id";
                        await using var cmd = conn.CreateCommand(updateSql);
                        cmd.AddParam("id", c.Id);
                        cmd.AddParam("code", c.Code);
                        cmd.AddParam("name", c.Name);
                        cmd.AddParam("type", c.Type);
                        cmd.AddParam("phone", c.Phone);
                        cmd.AddParam("email", c.Email);
                        cmd.AddParam("addr", c.Address);
                        cmd.AddParam("taxNo", c.TaxNo);
                        cmd.AddParam("taxOff", c.TaxOffice);
                        cmd.AddParam("bal", c.Balance);
                        cmd.AddParam("act", c.IsActive);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var insertSql = @"INSERT INTO CariAccounts (Id, Code, Name, Type, Phone, Email, Address, TaxNo, TaxOffice, Balance, IsActive, CreatedAt)
                                          VALUES (@id, @code, @name, @type, @phone, @email, @addr, @taxNo, @taxOff, @bal, @act, @created);";
                        await using var cmd = conn.CreateCommand(insertSql);
                        cmd.AddParam("id", c.Id);
                        cmd.AddParam("code", c.Code);
                        cmd.AddParam("name", c.Name);
                        cmd.AddParam("type", c.Type);
                        cmd.AddParam("phone", c.Phone);
                        cmd.AddParam("email", c.Email);
                        cmd.AddParam("addr", c.Address);
                        cmd.AddParam("taxNo", c.TaxNo);
                        cmd.AddParam("taxOff", c.TaxOffice);
                        cmd.AddParam("bal", c.Balance);
                        cmd.AddParam("act", c.IsActive);
                        cmd.AddParam("created", c.CreatedAt);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
        catch { }

        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(AccountsFile, json);
    }

    private async Task<List<CariTransaction>> LoadTransactionsInternalAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<CariTransaction>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = @"SELECT ct.Id, ct.CariAccountId, COALESCE(ca.Name, 'Cari') AS AccountName, ct.CreatedAt, ct.TransactionType, ct.DocumentNo, ct.Debit, ct.Credit, ct.BalanceAfter, ct.Description, ct.InvoiceId, ct.SaleId, ct.PaymentMethod
                            FROM CariTransactions ct
                            LEFT JOIN CariAccounts ca ON ct.CariAccountId = ca.Id
                            ORDER BY ct.CreatedAt DESC";
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new CariTransaction
                    {
                        Id = reader.GetInt64(0),
                        CariAccountId = reader.GetInt32(1),
                        CariAccountName = reader.GetString(2),
                        CreatedAt = reader.GetDateTime(3),
                        TransactionType = reader.GetString(4),
                        DocumentNo = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        Debit = reader.GetDecimal(6),
                        Credit = reader.GetDecimal(7),
                        BalanceAfter = reader.GetDecimal(8),
                        Description = reader.IsDBNull(9) ? "" : reader.GetString(9),
                        InvoiceId = reader.IsDBNull(10) ? null : reader.GetInt64(10),
                        SaleId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
                        PaymentMethod = reader.IsDBNull(12) ? null : reader.GetString(12)
                    });
                }
                if (list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch { }

        if (!File.Exists(TransactionsFile)) return new List<CariTransaction>();
        try
        {
            var json = await File.ReadAllTextAsync(TransactionsFile);
            return JsonSerializer.Deserialize<List<CariTransaction>>(json) ?? new List<CariTransaction>();
        }
        catch
        {
            return new List<CariTransaction>();
        }
    }

    private async Task SaveTransactionsInternalAsync(List<CariTransaction> list)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var t in list)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM CariTransactions WHERE Id = @id");
                    checkCmd.AddParam("id", t.Id);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (!exists)
                    {
                        var insertSql = @"INSERT INTO CariTransactions (Id, CariAccountId, CreatedAt, TransactionType, DocumentNo, Debit, Credit, BalanceAfter, Description, InvoiceId, SaleId, PaymentMethod)
                                          VALUES (@id, @cid, @created, @type, @doc, @deb, @cred, @bal, @desc, @invId, @saleId, @pm);";
                        await using var cmd = conn.CreateCommand(insertSql);
                        cmd.AddParam("id", t.Id);
                        cmd.AddParam("cid", t.CariAccountId);
                        cmd.AddParam("created", t.CreatedAt);
                        cmd.AddParam("type", t.TransactionType);
                        cmd.AddParam("doc", t.DocumentNo);
                        cmd.AddParam("deb", t.Debit);
                        cmd.AddParam("cred", t.Credit);
                        cmd.AddParam("bal", t.BalanceAfter);
                        cmd.AddParam("desc", t.Description);
                        cmd.AddParam("invId", t.InvoiceId);
                        cmd.AddParam("saleId", t.SaleId);
                        cmd.AddParam("pm", t.PaymentMethod);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
        catch { }

        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(TransactionsFile, json);
    }
}
