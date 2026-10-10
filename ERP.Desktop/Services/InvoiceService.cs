using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using System.Data.Common;

namespace ERP.Desktop.Services;

public class InvoiceService
{
    private readonly DatabaseService _dbService;
    private readonly WarehouseService _warehouseService;
    private readonly ProductService _productService;
    private readonly BankService _bankService;
    private readonly CashService _cashService;
    private readonly FinancialTransactionCoordinator _coordinator;

    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string InvoicesFile = Path.Combine(StorageDirectory, "invoices.json");
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public InvoiceService(
        WarehouseService? warehouseService = null,
        ProductService? productService = null,
        BankService? bankService = null,
        CashService? cashService = null,
        DatabaseService? dbService = null,
        FinancialTransactionCoordinator? coordinator = null)
    {
        _dbService = dbService ?? new DatabaseService();
        _warehouseService = warehouseService ?? new WarehouseService(_dbService);
        _productService = productService ?? new ProductService(_dbService);
        _bankService = bankService ?? new BankService(_dbService);
        _cashService = cashService ?? new CashService(_dbService);
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

    public async Task<List<Invoice>> GetAllInvoicesAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            return await LoadInvoicesInternalAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<Invoice>> GetInvoicesFilteredAsync(string? type = null, DateTime? startDate = null, DateTime? endDate = null, string? search = null)
    {
        var invoices = await GetAllInvoicesAsync();
        var query = invoices.AsQueryable();

        if (!string.IsNullOrWhiteSpace(type) && type != "Tümü")
        {
            query = query.Where(i => i.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }

        if (startDate.HasValue)
        {
            query = query.Where(i => i.InvoiceDate.Date >= startDate.Value.Date);
        }

        if (endDate.HasValue)
        {
            query = query.Where(i => i.InvoiceDate.Date <= endDate.Value.Date);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(i =>
                i.InvoiceNo.ToLowerInvariant().Contains(term) ||
                i.SupplierOrCustomer.ToLowerInvariant().Contains(term) ||
                i.WarehouseName.ToLowerInvariant().Contains(term));
        }

        return query.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.Id).ToList();
    }

    public async Task<string> GenerateNextInvoiceNumber(string type = "Alış Faturası")
    {
        var prefix = type == "Alış Faturası" ? "ALF" : (type == "Masraf Faturası" ? "MSF" : "STF");
        var year = DateTime.Today.Year;
        var invoices = await GetAllInvoicesAsync();

        var existingMatching = invoices
            .Where(i => i.InvoiceNo.StartsWith($"{prefix}-{year}-", StringComparison.OrdinalIgnoreCase))
            .Select(i =>
            {
                var parts = i.InvoiceNo.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int seq)) return seq;
                return 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}-{year}-{(existingMatching + 1):D4}";
    }

    public async Task<Invoice> SaveInvoiceAsync(Invoice invoice)
    {
        if (invoice.Items == null || invoice.Items.Count == 0)
        {
            throw new InvalidOperationException("Faturada en az 1 kalem ürün bulunmalıdır.");
        }

        // Tutar hesaplamaları
        decimal subTotal = 0;
        decimal vatTotal = 0;

        foreach (var item in invoice.Items)
        {
            subTotal += item.LineTotal;
            vatTotal += item.VatAmount;
        }

        invoice.SubTotal = Math.Round(subTotal, 2);
        invoice.VatTotal = Math.Round(vatTotal, 2);
        invoice.GrandTotal = Math.Round(subTotal + vatTotal, 2);

        // Depo bilgisini doğrula
        var warehouses = await _warehouseService.GetAllWarehousesAsync();
        var wh = warehouses.FirstOrDefault(w => w.Id == invoice.WarehouseId);
        if (wh != null)
        {
            invoice.WarehouseName = wh.Name;
        }

        // 1. Veritabanına ATOMİK OLARAK kaydet (Stok, Cari, Kasa, Banka tek transaction)
        if (await _dbService.TestConnectionAsync())
        {
            invoice = await _coordinator.SaveInvoiceAtomicAsync(invoice);
        }

        // 2. Yerel JSON'a yedek olarak kaydet
        await _semaphore.WaitAsync();
        try
        {
            var invoices = await LoadInvoicesInternalAsync();
            var idx = invoices.FindIndex(i => i.Id == invoice.Id);
            if (idx >= 0) invoices[idx] = invoice;
            else invoices.Add(invoice);

            var json = JsonSerializer.Serialize(invoices, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(InvoicesFile, json);
        }
        catch { }
        finally
        {
            _semaphore.Release();
        }

        return invoice;
    }

    public async Task<bool> DeleteInvoiceAsync(long invoiceId)
    {
        // 1. Veritabanından ve ilişkili hareketlerden (Stok, Cari, Finans) atomik olarak sil
        if (await _dbService.TestConnectionAsync())
        {
            await _coordinator.DeleteInvoiceAtomicAsync(invoiceId);
        }

        // 2. Yerel JSON'dan da kaldır
        await _semaphore.WaitAsync();
        try
        {
            var invoices = await LoadInvoicesInternalAsync();
            var invoice = invoices.FirstOrDefault(i => i.Id == invoiceId);
            if (invoice != null)
            {
                invoices.Remove(invoice);
                var json = JsonSerializer.Serialize(invoices, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(InvoicesFile, json);
            }
        }
        catch { }
        finally
        {
            _semaphore.Release();
        }

        return true;
    }

    public async Task<Invoice> CreateExpenseInvoiceAsync(
        string title,
        decimal amount,
        string category,
        string paymentType, // "Banka Hesabı" or "Nakit"
        int? bankAccountId = null,
        string? bankAccountName = null,
        DateTime? invoiceDate = null,
        string? notes = null)
    {
        var nextNo = await GenerateNextInvoiceNumber("Masraf Faturası");
        var invoice = new Invoice
        {
            InvoiceNo = nextNo,
            Type = "Masraf Faturası",
            InvoiceDate = invoiceDate ?? DateTime.Today,
            SupplierOrCustomer = title,
            SubTotal = amount,
            VatTotal = 0,
            GrandTotal = amount,
            PaymentType = paymentType,
            PaymentStatus = "Ödendi",
            BankAccountId = bankAccountId,
            BankAccountName = bankAccountName,
            Notes = notes ?? $"{category} Masraf Ödemesi: {title}",
            Items = new List<InvoiceItem>
            {
                new InvoiceItem
                {
                    ProductName = $"{category} - {title}",
                    Quantity = 1,
                    UnitPrice = amount,
                    VatRate = 0
                }
            }
        };

        return await SaveInvoiceAsync(invoice);
    }

    private async Task<List<Invoice>> LoadInvoicesInternalAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<Invoice>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                var sql = @"SELECT Id, InvoiceNo, Type, InvoiceDate, SupplierOrCustomer, TaxNo, WarehouseId, WarehouseName, SubTotal, VatTotal, GrandTotal, PaymentType, PaymentStatus, BankAccountId, BankAccountName, CurrentAccountName, Notes, CariAccountId, PaidAmount, RemainingAmount
                            FROM Invoices
                            ORDER BY InvoiceDate DESC, Id DESC";
                await using (var cmd = conn.CreateCommand(sql))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new Invoice
                        {
                            Id = reader.GetInt64(0),
                            InvoiceNo = reader.GetString(1),
                            Type = reader.GetString(2),
                            InvoiceDate = reader.GetDateTime(3),
                            SupplierOrCustomer = reader.GetString(4),
                            TaxNo = reader.IsDBNull(5) ? null : reader.GetString(5),
                            WarehouseId = reader.GetInt32(6),
                            WarehouseName = reader.GetString(7),
                            SubTotal = reader.GetDecimal(8),
                            VatTotal = reader.GetDecimal(9),
                            GrandTotal = reader.GetDecimal(10),
                            PaymentType = reader.GetString(11),
                            PaymentStatus = reader.GetString(12),
                            BankAccountId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                            BankAccountName = reader.IsDBNull(14) ? null : reader.GetString(14),
                            CurrentAccountName = reader.IsDBNull(15) ? null : reader.GetString(15),
                            Notes = reader.IsDBNull(16) ? null : reader.GetString(16),
                            CariAccountId = reader.IsDBNull(17) ? null : reader.GetInt32(17),
                            PaidAmount = reader.IsDBNull(18) ? 0m : reader.GetDecimal(18),
                            Items = new List<InvoiceItem>()
                        });
                    }
                }

                if (list.Count > 0)
                {
                    var itemsSql = @"SELECT Id, InvoiceId, ProductId, ProductCode, ProductName, Barcode, Quantity, UnitPrice, VatRate, LineTotal
                                     FROM InvoiceItems";
                    await using var itemCmd = conn.CreateCommand(itemsSql);
                    await using var itemReader = await itemCmd.ExecuteReaderAsync();

                    var itemsByInv = new Dictionary<long, List<InvoiceItem>>();
                    while (await itemReader.ReadAsync())
                    {
                        long invId = itemReader.GetInt64(1);
                        var item = new InvoiceItem
                        {
                            Id = itemReader.GetInt64(0),
                            InvoiceId = invId,
                            ProductId = itemReader.GetInt32(2),
                            ProductCode = itemReader.IsDBNull(3) ? "" : itemReader.GetString(3),
                            ProductName = itemReader.GetString(4),
                            Barcode = itemReader.IsDBNull(5) ? "" : itemReader.GetString(5),
                            Quantity = itemReader.GetDecimal(6),
                            UnitPrice = itemReader.GetDecimal(7),
                            VatRate = itemReader.GetDecimal(8)
                        };
                        if (!itemsByInv.TryGetValue(invId, out var itemList))
                        {
                            itemList = new List<InvoiceItem>();
                            itemsByInv[invId] = itemList;
                        }
                        itemList.Add(item);
                    }

                    foreach (var inv in list)
                    {
                        if (itemsByInv.TryGetValue(inv.Id, out var invItems))
                        {
                            inv.Items = invItems;
                        }
                    }

                    return list;
                }
            }
        }
        catch { }

        if (!File.Exists(InvoicesFile)) return new List<Invoice>();
        try
        {
            var json = await File.ReadAllTextAsync(InvoicesFile);
            return JsonSerializer.Deserialize<List<Invoice>>(json) ?? new List<Invoice>();
        }
        catch
        {
            return new List<Invoice>();
        }
    }

    private async Task SaveInvoicesInternalAsync(List<Invoice> list)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var inv in list)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM Invoices WHERE Id = @id");
                    checkCmd.AddParam("id", inv.Id);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (exists == 0)
                    {
                        var insSql = @"INSERT INTO Invoices (Id, InvoiceNo, Type, InvoiceDate, SupplierOrCustomer, TaxNo, WarehouseId, WarehouseName, SubTotal, VatTotal, GrandTotal, PaymentType, PaymentStatus, BankAccountId, BankAccountName, CurrentAccountName, Notes, CreatedAt)
                                       VALUES (@id, @no, @t, @date, @soc, @tax, @wid, @wn, @sub, @vat, @grand, @ptype, @pstat, @baid, @baname, @caname, @notes, GETDATE());";
                        await using var cmd = conn.CreateCommand(insSql);
                        cmd.AddParam("id", inv.Id);
                        cmd.AddParam("no", inv.InvoiceNo);
                        cmd.AddParam("t", inv.Type);
                        cmd.AddParam("date", inv.InvoiceDate);
                        cmd.AddParam("soc", inv.SupplierOrCustomer);
                        cmd.AddParam("tax", (object?)inv.TaxNo ?? DBNull.Value);
                        cmd.AddParam("wid", inv.WarehouseId);
                        cmd.AddParam("wn", inv.WarehouseName);
                        cmd.AddParam("sub", inv.SubTotal);
                        cmd.AddParam("vat", inv.VatTotal);
                        cmd.AddParam("grand", inv.GrandTotal);
                        cmd.AddParam("ptype", inv.PaymentType);
                        cmd.AddParam("pstat", inv.PaymentStatus);
                        cmd.AddParam("baid", (object?)inv.BankAccountId ?? DBNull.Value);
                        cmd.AddParam("baname", (object?)inv.BankAccountName ?? DBNull.Value);
                        cmd.AddParam("caname", (object?)inv.CurrentAccountName ?? DBNull.Value);
                        cmd.AddParam("notes", (object?)inv.Notes ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();

                        foreach (var item in inv.Items)
                        {
                            var itemSql = @"INSERT INTO InvoiceItems (InvoiceId, ProductId, ProductCode, ProductName, Barcode, Quantity, UnitPrice, VatRate, LineTotal)
                                            VALUES (@invid, @pid, @pc, @pn, @bc, @q, @up, @vat, @tot)";
                            await using var iCmd = conn.CreateCommand(itemSql);
                            iCmd.AddParam("invid", inv.Id);
                            iCmd.AddParam("pid", item.ProductId);
                            iCmd.AddParam("pc", (object?)item.ProductCode ?? DBNull.Value);
                            iCmd.AddParam("pn", item.ProductName);
                            iCmd.AddParam("bc", (object?)item.Barcode ?? DBNull.Value);
                            iCmd.AddParam("q", item.Quantity);
                            iCmd.AddParam("up", item.UnitPrice);
                            iCmd.AddParam("vat", item.VatRate);
                            iCmd.AddParam("tot", item.LineTotal);
                            await iCmd.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
        }
        catch { }

        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(InvoicesFile, json);
    }
}
