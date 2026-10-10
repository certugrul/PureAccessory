using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using System.Data.Common;

namespace ERP.Desktop.Services;

/// <summary>
/// ERP Sisteminin tek ve merkezi işlem koordinatörüdür.
/// Fatura, Kasa, Banka, Cari, Ürün ve Depo/Stok hareketlerinin
/// tek bir merkezi kaynaktan, atomik (SqlTransaction) ve mükerrersiz
/// yürütülmesini garanti eder.
/// </summary>
public class FinancialTransactionCoordinator
{
    private readonly DatabaseService _dbService;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public FinancialTransactionCoordinator(DatabaseService? dbService = null)
    {
        _dbService = dbService ?? new DatabaseService();
    }

    #region Kasa Bakiyesi (Merkezi Hesaplama)

    /// <summary>
    /// Kasadaki toplam nakit bakiyeyi merkezi CashTransactions hareketlerinden hesaplar.
    /// Hiçbir harici tablo veya bağımsız bakiye değişkeni kullanmaz.
    /// </summary>
    public async Task<decimal> GetCentralCashBalanceAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                var startDate = ThemeService.Current?.CashBalanceStartDate ?? new DateTime(2026, 10, 8);

                var sql = @"
                    SELECT ISNULL(SUM(
                        CASE 
                            WHEN Type IN (
                                'Giriş', 'Nakit Giriş', 'Nakit Satış', 'Bankadan Transfer', 
                                'Tahsilat', 'Nakit Tahsilat', 'Nakit Tahsilat (Cari)', 
                                'Nakit Giriş (Fatura)', 'Nakit Giriş (Fatura İptal)',
                                'Kasa Açılış Bakiyesi', 'Kasa Girişi', 'Kasa Düzeltme (+)'
                            ) THEN Amount
                            WHEN Type IN (
                                'Çıkış', 'Nakit Çıkış', 'Bankaya Transfer', 
                                'Ödeme', 'Nakit Ödeme', 'Nakit Ödeme (Cari)', 
                                'Nakit Çıkış (Fatura)', 'Nakit Çıkış (Fatura İptal)',
                                'Masraf', 'Gider', 'Nakit Masraf / Gider', 'Kasa Düzeltme (-)'
                            ) OR Type LIKE '%Çıkış%' OR Type LIKE '%Gider%' THEN -Amount
                            ELSE Amount
                        END
                    ), 0)
                    FROM CashTransactions
                    WHERE CreatedAt >= @startDate;";

                await using var cmd = conn.CreateCommand(sql);
                cmd.AddParam("startDate", startDate.Date);
                var result = await cmd.ExecuteScalarAsync();
                decimal balance = result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0m;

                // CashAccounts (Id = 1) tablosunu da anında güncel tut
                try
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM CashAccounts WHERE Id = 1");
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (exists > 0)
                    {
                        using var updCmd = conn.CreateCommand("UPDATE CashAccounts SET Balance = @bal WHERE Id = 1");
                        updCmd.AddParam("bal", balance);
                        await updCmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        using var insCmd = conn.CreateCommand("INSERT INTO CashAccounts (Id, Name, Balance) VALUES (1, 'Merkez Kasa', @bal)");
                        insCmd.AddParam("bal", balance);
                        await insCmd.ExecuteNonQueryAsync();
                    }
                }
                catch { }

                return balance;
            }
        }
        catch { }

        // Fallback: Yerel JSON
        var cashService = new CashService(_dbService);
        return await cashService.GetTotalCashBalanceFallbackAsync();
    }

    #endregion

    #region Fatura İşlem Motoru (Atomik & Idempotent)

    /// <summary>
    /// Bir faturayı kaydederken Fatura + Ürün/Depo Stok + Cari Hesap + Kasa/Banka hareketlerini
    /// tek bir veritabanı Transaction'ı içinde atomik olarak yürütür.
    /// </summary>
    public async Task<Invoice> SaveInvoiceAtomicAsync(Invoice invoice)
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

        if (invoice.PaymentStatus == "Ödendi")
        {
            invoice.PaidAmount = invoice.GrandTotal;
        }
        else
        {
            invoice.PaidAmount = 0;
        }

        await _semaphore.WaitAsync();
        try
        {
            bool dbConnected = await _dbService.TestConnectionAsync();
            if (!dbConnected)
            {
                throw new InvalidOperationException("Veritabanı bağlantısı kurulamadı. Fatura kaydedilemez.");
            }

            await using var conn = _dbService.CreateConnection();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                // 1. Eğer var olan bir fatura güncelleniyorsa, eski hareketlerini temizle (Idempotent)
                if (invoice.Id > 0)
                {
                    await RevertPreviousInvoiceMovementsInternalAsync(invoice.Id, conn, tx);
                }

                // 2. Fatura Ana Kaydını Kaydet / Güncelle
                if (invoice.Id == 0)
                {
                    var insSql = @"
                        INSERT INTO Invoices (
                            InvoiceNo, Type, InvoiceDate, SupplierOrCustomer, TaxNo,
                            WarehouseId, WarehouseName, SubTotal, VatTotal, GrandTotal,
                            PaymentType, PaymentStatus, BankAccountId, BankAccountName,
                            CurrentAccountName, Notes, CariAccountId, PaidAmount, RemainingAmount, CreatedAt
                        )
                        OUTPUT INSERTED.Id
                        VALUES (
                            @no, @t, @date, @soc, @tax,
                            @wid, @wn, @sub, @vat, @grand,
                            @ptype, @pstat, @baid, @baname,
                            @caname, @notes, @caid, @paid, @rem, GETDATE()
                        );";

                    await using var insCmd = conn.CreateCommand(insSql, tx);
                    insCmd.AddParam("no", invoice.InvoiceNo);
                    insCmd.AddParam("t", invoice.Type);
                    insCmd.AddParam("date", invoice.InvoiceDate);
                    insCmd.AddParam("soc", invoice.SupplierOrCustomer);
                    insCmd.AddParam("tax", (object?)invoice.TaxNo ?? DBNull.Value);
                    insCmd.AddParam("wid", invoice.WarehouseId);
                    insCmd.AddParam("wn", invoice.WarehouseName);
                    insCmd.AddParam("sub", invoice.SubTotal);
                    insCmd.AddParam("vat", invoice.VatTotal);
                    insCmd.AddParam("grand", invoice.GrandTotal);
                    insCmd.AddParam("ptype", invoice.PaymentType);
                    insCmd.AddParam("pstat", invoice.PaymentStatus);
                    insCmd.AddParam("baid", (object?)invoice.BankAccountId ?? DBNull.Value);
                    insCmd.AddParam("baname", (object?)invoice.BankAccountName ?? DBNull.Value);
                    insCmd.AddParam("caname", (object?)invoice.CurrentAccountName ?? DBNull.Value);
                    insCmd.AddParam("notes", (object?)invoice.Notes ?? DBNull.Value);
                    insCmd.AddParam("caid", (object?)invoice.CariAccountId ?? DBNull.Value);
                    insCmd.AddParam("paid", invoice.PaidAmount);
                    insCmd.AddParam("rem", invoice.RemainingAmount);

                    invoice.Id = Convert.ToInt64(await insCmd.ExecuteScalarAsync());
                }
                else
                {
                    var updSql = @"
                        UPDATE Invoices SET
                            InvoiceNo = @no, Type = @t, InvoiceDate = @date, SupplierOrCustomer = @soc, TaxNo = @tax,
                            WarehouseId = @wid, WarehouseName = @wn, SubTotal = @sub, VatTotal = @vat, GrandTotal = @grand,
                            PaymentType = @ptype, PaymentStatus = @pstat, BankAccountId = @baid, BankAccountName = @baname,
                            CurrentAccountName = @caname, Notes = @notes, CariAccountId = @caid, PaidAmount = @paid, RemainingAmount = @rem
                        WHERE Id = @id;";

                    await using var updCmd = conn.CreateCommand(updSql, tx);
                    updCmd.AddParam("id", invoice.Id);
                    updCmd.AddParam("no", invoice.InvoiceNo);
                    updCmd.AddParam("t", invoice.Type);
                    updCmd.AddParam("date", invoice.InvoiceDate);
                    updCmd.AddParam("soc", invoice.SupplierOrCustomer);
                    updCmd.AddParam("tax", (object?)invoice.TaxNo ?? DBNull.Value);
                    updCmd.AddParam("wid", invoice.WarehouseId);
                    updCmd.AddParam("wn", invoice.WarehouseName);
                    updCmd.AddParam("sub", invoice.SubTotal);
                    updCmd.AddParam("vat", invoice.VatTotal);
                    updCmd.AddParam("grand", invoice.GrandTotal);
                    updCmd.AddParam("ptype", invoice.PaymentType);
                    updCmd.AddParam("pstat", invoice.PaymentStatus);
                    updCmd.AddParam("baid", (object?)invoice.BankAccountId ?? DBNull.Value);
                    updCmd.AddParam("baname", (object?)invoice.BankAccountName ?? DBNull.Value);
                    updCmd.AddParam("caname", (object?)invoice.CurrentAccountName ?? DBNull.Value);
                    updCmd.AddParam("notes", (object?)invoice.Notes ?? DBNull.Value);
                    updCmd.AddParam("caid", (object?)invoice.CariAccountId ?? DBNull.Value);
                    updCmd.AddParam("paid", invoice.PaidAmount);
                    updCmd.AddParam("rem", invoice.RemainingAmount);
                    await updCmd.ExecuteNonQueryAsync();

                    // Eski kalemleri sil
                    using var delItemsCmd = conn.CreateCommand("DELETE FROM InvoiceItems WHERE InvoiceId = @invId", tx);
                    delItemsCmd.AddParam("invId", invoice.Id);
                    await delItemsCmd.ExecuteNonQueryAsync();
                }

                // 3. Fatura Kalemlerini Ekle
                foreach (var item in invoice.Items)
                {
                    item.InvoiceId = invoice.Id;
                    var itemSql = @"
                        INSERT INTO InvoiceItems (InvoiceId, ProductId, ProductCode, ProductName, Barcode, Quantity, UnitPrice, VatRate, LineTotal)
                        VALUES (@invId, @pid, @pcode, @pname, @barcode, @qty, @price, @vat, @total);";
                    await using var itemCmd = conn.CreateCommand(itemSql, tx);
                    itemCmd.AddParam("invId", invoice.Id);
                    itemCmd.AddParam("pid", item.ProductId);
                    itemCmd.AddParam("pcode", (object?)item.ProductCode ?? DBNull.Value);
                    itemCmd.AddParam("pname", item.ProductName);
                    itemCmd.AddParam("barcode", (object?)item.Barcode ?? DBNull.Value);
                    itemCmd.AddParam("qty", item.Quantity);
                    itemCmd.AddParam("price", item.UnitPrice);
                    itemCmd.AddParam("vat", item.VatRate);
                    itemCmd.AddParam("total", item.LineTotal);
                    await itemCmd.ExecuteNonQueryAsync();
                }

                // 4. Depo ve Stok Hareketlerini Uygula
                bool isPurchase = invoice.Type == "Alış Faturası";
                bool isSale = invoice.Type == "Satış Faturası";

                if (isPurchase || isSale)
                {
                    foreach (var item in invoice.Items)
                    {
                        if (item.ProductId <= 0 || item.Quantity <= 0) continue;

                        decimal stockDelta = isPurchase ? item.Quantity : -item.Quantity;
                        string movType = isPurchase ? "Alış Faturası Girişi" : "Satış Faturası Çıkışı";
                        string movDesc = $"{invoice.Type}: {invoice.SupplierOrCustomer} ({invoice.InvoiceNo})";

                        // WarehouseStocks güncelle
                        using var checkStockCmd = conn.CreateCommand("SELECT COUNT(*) FROM WarehouseStocks WHERE WarehouseId = @wid AND ProductId = @pid", tx);
                        checkStockCmd.AddParam("wid", invoice.WarehouseId);
                        checkStockCmd.AddParam("pid", item.ProductId);
                        int stockExists = Convert.ToInt32(await checkStockCmd.ExecuteScalarAsync());

                        if (stockExists > 0)
                        {
                            var stockSql = "UPDATE WarehouseStocks SET Quantity = Quantity + @delta WHERE WarehouseId = @wid AND ProductId = @pid;";
                            await using var stockCmd = conn.CreateCommand(stockSql, tx);
                            stockCmd.AddParam("wid", invoice.WarehouseId);
                            stockCmd.AddParam("pid", item.ProductId);
                            stockCmd.AddParam("delta", stockDelta);
                            await stockCmd.ExecuteNonQueryAsync();
                        }
                        else
                        {
                            var stockSql = "INSERT INTO WarehouseStocks (WarehouseId, ProductId, Quantity) VALUES (@wid, @pid, @delta);";
                            await using var stockCmd = conn.CreateCommand(stockSql, tx);
                            stockCmd.AddParam("wid", invoice.WarehouseId);
                            stockCmd.AddParam("pid", item.ProductId);
                            stockCmd.AddParam("delta", stockDelta);
                            await stockCmd.ExecuteNonQueryAsync();
                        }

                        // StockMovements kaydı
                        var smSql = @"
                            INSERT INTO StockMovements (
                                WarehouseId, ProductId, Type, Quantity, Description, 
                                DocumentNo, Category, CreatedAt, SourceDocumentType, SourceDocumentId
                            )
                            VALUES (
                                @wid, @pid, @type, @qty, @desc, 
                                @doc, @cat, @created, 'INVOICE', @invId
                            );";
                        await using var smCmd = conn.CreateCommand(smSql, tx);
                        smCmd.AddParam("wid", invoice.WarehouseId);
                        smCmd.AddParam("pid", item.ProductId);
                        smCmd.AddParam("type", movType);
                        smCmd.AddParam("qty", item.Quantity);
                        smCmd.AddParam("desc", movDesc);
                        smCmd.AddParam("doc", invoice.InvoiceNo);
                        smCmd.AddParam("cat", isPurchase ? "Giriş" : "Çıkış");
                        smCmd.AddParam("created", invoice.InvoiceDate);
                        smCmd.AddParam("invId", invoice.Id);
                        await smCmd.ExecuteNonQueryAsync();

                        // Alış faturası ise ürün alış fiyatını güncelle
                        if (isPurchase && item.UnitPrice > 0)
                        {
                            var prodUpdSql = "UPDATE Products SET PurchasePrice = @pp WHERE Id = @pid";
                            await using var prodUpdCmd = conn.CreateCommand(prodUpdSql, tx);
                            prodUpdCmd.AddParam("pp", item.UnitPrice);
                            prodUpdCmd.AddParam("pid", item.ProductId);
                            await prodUpdCmd.ExecuteNonQueryAsync();
                        }
                    }
                }

                // 5. Cari Hesap Entegrasyonu
                if (invoice.CariAccountId.HasValue && invoice.CariAccountId.Value > 0)
                {
                    int caId = invoice.CariAccountId.Value;

                    if (isSale)
                    {
                        // Satış faturası -> Müşteri Borçlanır (Debit)
                        decimal debit = invoice.GrandTotal;
                        decimal credit = 0;
                        string desc = $"Satış Faturası: {invoice.InvoiceNo}";

                        await InsertCariTransactionInternalAsync(conn, tx, caId, "Açık Hesap Fatura", invoice.InvoiceNo, debit, credit, desc, invoice.Id, null, "Açık Hesap", invoice.InvoiceDate);

                        // Eğer fatura 'Ödendi' ise anında tahsilat kaydı da oluştur
                        if (invoice.PaymentStatus == "Ödendi")
                        {
                            string payDesc = $"Fatura Tahsilatı ({invoice.PaymentType}): {invoice.InvoiceNo}";
                            await InsertCariTransactionInternalAsync(conn, tx, caId, "Tahsilat", invoice.InvoiceNo, 0, invoice.GrandTotal, payDesc, invoice.Id, null, invoice.PaymentType, invoice.InvoiceDate);
                        }
                    }
                    else if (isPurchase)
                    {
                        // Alış faturası -> Tedarikçi Alacaklanır (Credit)
                        decimal debit = 0;
                        decimal credit = invoice.GrandTotal;
                        string desc = $"Alış Faturası: {invoice.InvoiceNo}";

                        await InsertCariTransactionInternalAsync(conn, tx, caId, "Açık Hesap Fatura", invoice.InvoiceNo, debit, credit, desc, invoice.Id, null, "Açık Hesap", invoice.InvoiceDate);

                        // Eğer fatura 'Ödendi' ise tedarikçiye tediye/ödeme kaydı da oluştur
                        if (invoice.PaymentStatus == "Ödendi")
                        {
                            string payDesc = $"Fatura Ödemesi ({invoice.PaymentType}): {invoice.InvoiceNo}";
                            await InsertCariTransactionInternalAsync(conn, tx, caId, "Ödeme", invoice.InvoiceNo, invoice.GrandTotal, 0, payDesc, invoice.Id, null, invoice.PaymentType, invoice.InvoiceDate);
                        }
                    }
                    else if (invoice.Type == "Masraf Faturası")
                    {
                        // Masraf faturası cariyle ilişkili ise borç/alacak kaydet
                        if (invoice.PaymentStatus == "Ödendi")
                        {
                            string payDesc = $"Masraf Ödemesi ({invoice.PaymentType}): {invoice.InvoiceNo}";
                            await InsertCariTransactionInternalAsync(conn, tx, caId, "Masraf", invoice.InvoiceNo, invoice.GrandTotal, 0, payDesc, invoice.Id, null, invoice.PaymentType, invoice.InvoiceDate);
                        }
                    }
                }

                // 6. Finans (Kasa / Banka) Hareketi
                if (invoice.PaymentStatus == "Ödendi" && invoice.GrandTotal > 0)
                {
                    if (invoice.PaymentType == "Nakit")
                    {
                        string cashType;
                        if (isPurchase || invoice.Type == "Masraf Faturası")
                        {
                            cashType = invoice.Type == "Masraf Faturası" ? "Nakit Masraf / Gider" : "Nakit Çıkış (Fatura)";
                        }
                        else
                        {
                            cashType = "Nakit Giriş (Fatura)";
                        }

                        var cashSql = @"
                            INSERT INTO CashTransactions (
                                CashAccountId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId
                            )
                            VALUES (
                                1, @type, @amt, @desc, @created, 'INVOICE', @invId
                            );";
                        await using var cashCmd = conn.CreateCommand(cashSql, tx);
                        cashCmd.AddParam("type", cashType);
                        cashCmd.AddParam("amt", invoice.GrandTotal);
                        cashCmd.AddParam("desc", $"{invoice.Type}: {invoice.SupplierOrCustomer} ({invoice.InvoiceNo})");
                        cashCmd.AddParam("created", invoice.InvoiceDate);
                        cashCmd.AddParam("invId", invoice.Id);
                        await cashCmd.ExecuteNonQueryAsync();
                    }
                    else if (invoice.PaymentType == "Banka Hesabı" && invoice.BankAccountId.HasValue && invoice.BankAccountId.Value > 0)
                    {
                        int baId = invoice.BankAccountId.Value;
                        string direction = (isPurchase || invoice.Type == "Masraf Faturası") ? "Çıkış" : "Giriş";
                        string txType = invoice.Type == "Masraf Faturası" ? "Masraf / Gider" : (isPurchase ? "Fatura Ödemesi" : "Fatura Tahsilatı");
                        decimal inflow = direction == "Giriş" ? invoice.GrandTotal : 0;
                        decimal outflow = direction == "Çıkış" ? invoice.GrandTotal : 0;

                        // BankAccount bakiyesini güncelle
                        var bAccSql = @"
                            UPDATE BankAccounts 
                            SET CurrentBalance = CurrentBalance + @delta,
                                ActualBalance = ActualBalance + @delta
                            WHERE Id = @baId;";
                        await using var bAccCmd = conn.CreateCommand(bAccSql, tx);
                        bAccCmd.AddParam("delta", inflow - outflow);
                        bAccCmd.AddParam("baId", baId);
                        await bAccCmd.ExecuteNonQueryAsync();

                        decimal balAfter = 0;
                        string baName = invoice.BankAccountName ?? "Banka Hesabı";
                        using var readBalCmd = conn.CreateCommand("SELECT CurrentBalance, BankName, AccountName FROM BankAccounts WHERE Id = @baId", tx);
                        readBalCmd.AddParam("baId", baId);
                        await using (var bReader = await readBalCmd.ExecuteReaderAsync())
                        {
                            if (await bReader.ReadAsync())
                            {
                                balAfter = bReader.GetDecimal(0);
                                baName = $"{bReader.GetString(1)} - {bReader.GetString(2)}";
                            }
                        }

                        var btSql = @"
                            INSERT INTO BankTransactions (
                                BankAccountId, BankAccountName, TransactionType, Direction,
                                Inflow, Outflow, BalanceAfter, Description, PaymentMethod,
                                ReferenceId, ReferenceType, ""User"", CreatedAt
                            )
                            VALUES (
                                @baId, @baName, @tt, @dir,
                                @inf, @outf, @bal, @desc, 'Banka / Havale',
                                @refId, 'FATURA', 'Yönetici', @created
                            );";
                        await using var btCmd = conn.CreateCommand(btSql, tx);
                        btCmd.AddParam("baId", baId);
                        btCmd.AddParam("baName", baName);
                        btCmd.AddParam("tt", txType);
                        btCmd.AddParam("dir", direction);
                        btCmd.AddParam("inf", inflow);
                        btCmd.AddParam("outf", outflow);
                        btCmd.AddParam("bal", balAfter);
                        btCmd.AddParam("desc", $"{invoice.Type}: {invoice.SupplierOrCustomer} ({invoice.InvoiceNo})");
                        btCmd.AddParam("refId", invoice.Id);
                        btCmd.AddParam("created", invoice.InvoiceDate);
                        await btCmd.ExecuteNonQueryAsync();
                    }
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            return invoice;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Bir faturayı ve oluşturduğu tüm stok, cari ve finans hareketlerini atomik olarak siler / geri alır.
    /// </summary>
    public async Task<bool> DeleteInvoiceAtomicAsync(long invoiceId)
    {
        await _semaphore.WaitAsync();
        try
        {
            await using var conn = _dbService.CreateConnection();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                // 1. Önceki hareketleri tersine çevir
                await RevertPreviousInvoiceMovementsInternalAsync(invoiceId, conn, tx);

                // 2. Kalemleri ve faturayı sil
                using var delItemsCmd = conn.CreateCommand("DELETE FROM InvoiceItems WHERE InvoiceId = @invId", tx);
                delItemsCmd.AddParam("invId", invoiceId);
                await delItemsCmd.ExecuteNonQueryAsync();

                using var delInvCmd = conn.CreateCommand("DELETE FROM Invoices WHERE Id = @invId", tx);
                delInvCmd.AddParam("invId", invoiceId);
                int affected = await delInvCmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();
                return affected > 0;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Faturaya ait eski stok, cari, kasa ve banka kayıtlarını temizler/tersine çevirir.
    /// </summary>
    private static async Task RevertPreviousInvoiceMovementsInternalAsync(long invoiceId, DbConnection conn, DbTransaction tx)
    {
        // 1. Stok hareketlerini tersine çevir
        var getSmSql = "SELECT WarehouseId, ProductId, Type, Quantity FROM StockMovements WHERE SourceDocumentType = 'INVOICE' AND SourceDocumentId = @invId";
        var stockReverts = new List<(int WarehouseId, int ProductId, string Type, decimal Quantity)>();
        await using (var getSmCmd = conn.CreateCommand(getSmSql, tx))
        {
            getSmCmd.AddParam("invId", invoiceId);
            await using var smReader = await getSmCmd.ExecuteReaderAsync();
            while (await smReader.ReadAsync())
            {
                stockReverts.Add((
                    smReader.GetInt32(0),
                    smReader.GetInt32(1),
                    smReader.GetString(2),
                    smReader.GetDecimal(3)
                ));
            }
        }

        foreach (var sr in stockReverts)
        {
            // Giriş yapılmışsa depodan düş, çıkış yapılmışsa depoya geri ekle
            decimal reverseDelta = sr.Type.Contains("Giriş") ? -sr.Quantity : sr.Quantity;
            using var revStockCmd = conn.CreateCommand("UPDATE WarehouseStocks SET Quantity = Quantity + @delta WHERE WarehouseId = @wid AND ProductId = @pid", tx);
            revStockCmd.AddParam("delta", reverseDelta);
            revStockCmd.AddParam("wid", sr.WarehouseId);
            revStockCmd.AddParam("pid", sr.ProductId);
            await revStockCmd.ExecuteNonQueryAsync();
        }

        using var delSmCmd = conn.CreateCommand("DELETE FROM StockMovements WHERE SourceDocumentType = 'INVOICE' AND SourceDocumentId = @invId", tx);
        delSmCmd.AddParam("invId", invoiceId);
        await delSmCmd.ExecuteNonQueryAsync();

        // 2. Cari hareketlerini temizle ve cari bakiyesini düzelt
        var getCtSql = "SELECT CariAccountId, Debit, Credit FROM CariTransactions WHERE InvoiceId = @invId";
        var cariReverts = new List<(int CariAccountId, decimal Debit, decimal Credit)>();
        await using (var getCtCmd = conn.CreateCommand(getCtSql, tx))
        {
            getCtCmd.AddParam("invId", invoiceId);
            await using var ctReader = await getCtCmd.ExecuteReaderAsync();
            while (await ctReader.ReadAsync())
            {
                cariReverts.Add((ctReader.GetInt32(0), ctReader.GetDecimal(1), ctReader.GetDecimal(2)));
            }
        }

        foreach (var cr in cariReverts)
        {
            // Borç yazılmışsa düş, alacak yazılmışsa ekle
            decimal balDelta = -(cr.Debit - cr.Credit);
            using var revCariCmd = conn.CreateCommand("UPDATE CariAccounts SET Balance = Balance + @delta WHERE Id = @caId", tx);
            revCariCmd.AddParam("delta", balDelta);
            revCariCmd.AddParam("caId", cr.CariAccountId);
            await revCariCmd.ExecuteNonQueryAsync();
        }

        using var delCtCmd = conn.CreateCommand("DELETE FROM CariTransactions WHERE InvoiceId = @invId", tx);
        delCtCmd.AddParam("invId", invoiceId);
        await delCtCmd.ExecuteNonQueryAsync();

        // 3. Kasa hareketini sil
        using var delCashCmd = conn.CreateCommand("DELETE FROM CashTransactions WHERE SourceDocumentType = 'INVOICE' AND SourceDocumentId = @invId", tx);
        delCashCmd.AddParam("invId", invoiceId);
        await delCashCmd.ExecuteNonQueryAsync();

        // 4. Banka hareketini tersine çevir ve sil
        var getBtSql = "SELECT BankAccountId, Inflow, Outflow FROM BankTransactions WHERE ReferenceType = 'FATURA' AND ReferenceId = @invId";
        var bankReverts = new List<(int BankAccountId, decimal Inflow, decimal Outflow)>();
        await using (var getBtCmd = conn.CreateCommand(getBtSql, tx))
        {
            getBtCmd.AddParam("invId", invoiceId);
            await using var btReader = await getBtCmd.ExecuteReaderAsync();
            while (await btReader.ReadAsync())
            {
                bankReverts.Add((btReader.GetInt32(0), btReader.GetDecimal(1), btReader.GetDecimal(2)));
            }
        }

        foreach (var br in bankReverts)
        {
            decimal delta = -(br.Inflow - br.Outflow);
            using var revBankCmd = conn.CreateCommand("UPDATE BankAccounts SET CurrentBalance = CurrentBalance + @delta, ActualBalance = ActualBalance + @delta WHERE Id = @baId", tx);
            revBankCmd.AddParam("delta", delta);
            revBankCmd.AddParam("baId", br.BankAccountId);
            await revBankCmd.ExecuteNonQueryAsync();
        }

        using var delBtCmd = conn.CreateCommand("DELETE FROM BankTransactions WHERE ReferenceType = 'FATURA' AND ReferenceId = @invId", tx);
        delBtCmd.AddParam("invId", invoiceId);
        await delBtCmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertCariTransactionInternalAsync(
        DbConnection conn,
        DbTransaction tx,
        int cariAccountId,
        string transactionType,
        string documentNo,
        decimal debit,
        decimal credit,
        string description,
        long? invoiceId,
        long? saleId,
        string? paymentMethod,
        DateTime date)
    {
        // Cari bakiyesini güncelle ve yeni bakiyeyi al
        var updCariSql = "UPDATE CariAccounts SET Balance = Balance + @delta WHERE Id = @id;";
        await using var updCmd = conn.CreateCommand(updCariSql, tx);
        updCmd.AddParam("delta", debit - credit);
        updCmd.AddParam("id", cariAccountId);
        await updCmd.ExecuteNonQueryAsync();

        using var readBalCmd = conn.CreateCommand("SELECT Balance FROM CariAccounts WHERE Id = @id", tx);
        readBalCmd.AddParam("id", cariAccountId);
        var balObj = await readBalCmd.ExecuteScalarAsync();
        decimal newBalance = balObj != null && balObj != DBNull.Value ? Convert.ToDecimal(balObj) : 0m;

        var insSql = @"
            INSERT INTO CariTransactions (
                CariAccountId, CreatedAt, TransactionType, DocumentNo, Debit, Credit, BalanceAfter, Description, InvoiceId, SaleId, PaymentMethod
            )
            VALUES (
                @caId, @created, @type, @doc, @deb, @cred, @bal, @desc, @invId, @saleId, @pm
            );";

        await using var insCmd = conn.CreateCommand(insSql, tx);
        insCmd.AddParam("caId", cariAccountId);
        insCmd.AddParam("created", date);
        insCmd.AddParam("type", transactionType);
        insCmd.AddParam("doc", (object?)documentNo ?? DBNull.Value);
        insCmd.AddParam("deb", debit);
        insCmd.AddParam("cred", credit);
        insCmd.AddParam("bal", newBalance);
        insCmd.AddParam("desc", (object?)description ?? DBNull.Value);
        insCmd.AddParam("invId", (object?)invoiceId ?? DBNull.Value);
        insCmd.AddParam("saleId", (object?)saleId ?? DBNull.Value);
        insCmd.AddParam("pm", (object?)paymentMethod ?? DBNull.Value);
        await insCmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Cari Tahsilat ve Tediye Motoru

    /// <summary>
    /// Cari hesap tahsilat veya ödeme kaydını yürütür.
    /// Ödeme yöntemine göre otomatik olarak Kasa veya Banka hareketini oluşturur.
    /// </summary>
    public async Task<CariTransaction> ProcessCariTransactionAsync(
        int cariAccountId,
        string transactionType,
        decimal debit,
        decimal credit,
        string documentNo,
        string description,
        string paymentMethod = "Nakit",
        int? bankAccountId = null,
        DateTime? customDate = null)
    {
        var date = customDate ?? DateTime.Now;

        await _semaphore.WaitAsync();
        try
        {
            await using var conn = _dbService.CreateConnection();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                // 1. Cari bakiyesini güncelle
                var updCariSql = "UPDATE CariAccounts SET Balance = Balance + @delta WHERE Id = @id;";
                await using var updCmd = conn.CreateCommand(updCariSql, tx);
                updCmd.AddParam("delta", debit - credit);
                updCmd.AddParam("id", cariAccountId);
                await updCmd.ExecuteNonQueryAsync();

                using var readBalCmd = conn.CreateCommand("SELECT Balance FROM CariAccounts WHERE Id = @id", tx);
                readBalCmd.AddParam("id", cariAccountId);
                var balObj = await readBalCmd.ExecuteScalarAsync();
                decimal newBalance = balObj != null && balObj != DBNull.Value ? Convert.ToDecimal(balObj) : 0m;

                // 2. CariTransactions ekle
                var insSql = @"
                    INSERT INTO CariTransactions (
                        CariAccountId, CreatedAt, TransactionType, DocumentNo, Debit, Credit, BalanceAfter, Description, PaymentMethod
                    )
                    OUTPUT INSERTED.Id
                    VALUES (
                        @caId, @created, @type, @doc, @deb, @cred, @bal, @desc, @pm
                    );";

                await using var insCmd = conn.CreateCommand(insSql, tx);
                insCmd.AddParam("caId", cariAccountId);
                insCmd.AddParam("created", date);
                insCmd.AddParam("type", transactionType);
                insCmd.AddParam("doc", (object?)documentNo ?? DBNull.Value);
                insCmd.AddParam("deb", debit);
                insCmd.AddParam("cred", credit);
                insCmd.AddParam("bal", newBalance);
                insCmd.AddParam("desc", (object?)description ?? DBNull.Value);
                insCmd.AddParam("pm", (object?)paymentMethod ?? DBNull.Value);

                long cariTxId = Convert.ToInt64(await insCmd.ExecuteScalarAsync());

                // 3. Finans Hareketi (Kasa veya Banka)
                if (paymentMethod == "Nakit")
                {
                    if (credit > 0)
                    {
                        // Müşteriden nakit tahsilat -> Kasa Girişi
                        var cashSql = @"
                            INSERT INTO CashTransactions (CashAccountId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId)
                            VALUES (1, 'Nakit Tahsilat (Cari)', @amt, @desc, @created, 'CARI', @cid);";
                        await using var cashCmd = conn.CreateCommand(cashSql, tx);
                        cashCmd.AddParam("amt", credit);
                        cashCmd.AddParam("desc", string.IsNullOrWhiteSpace(description) ? "Cari Hesaptan Nakit Tahsilat" : description);
                        cashCmd.AddParam("created", date);
                        cashCmd.AddParam("cid", cariTxId);
                        await cashCmd.ExecuteNonQueryAsync();
                    }
                    else if (debit > 0 && (transactionType.Contains("Ödeme") || transactionType.Contains("Tediye")))
                    {
                        // Tedarikçiye nakit ödeme -> Kasa Çıkışı
                        var cashSql = @"
                            INSERT INTO CashTransactions (CashAccountId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId)
                            VALUES (1, 'Nakit Ödeme (Cari)', @amt, @desc, @created, 'CARI', @cid);";
                        await using var cashCmd = conn.CreateCommand(cashSql, tx);
                        cashCmd.AddParam("amt", debit);
                        cashCmd.AddParam("desc", string.IsNullOrWhiteSpace(description) ? "Cari Hesaba Nakit Ödeme" : description);
                        cashCmd.AddParam("created", date);
                        cashCmd.AddParam("cid", cariTxId);
                        await cashCmd.ExecuteNonQueryAsync();
                    }
                }
                else if (paymentMethod == "Banka Hesabı" && bankAccountId.HasValue && bankAccountId.Value > 0)
                {
                    int baId = bankAccountId.Value;
                    if (credit > 0)
                    {
                        // Banka Girişi
                        var bAccSql = "UPDATE BankAccounts SET CurrentBalance = CurrentBalance + @amt, ActualBalance = ActualBalance + @amt WHERE Id = @baId";
                        await using var bAccCmd = conn.CreateCommand(bAccSql, tx);
                        bAccCmd.AddParam("amt", credit);
                        bAccCmd.AddParam("baId", baId);
                        await bAccCmd.ExecuteNonQueryAsync();

                        string baName = "Banka Hesabı";
                        decimal balAfter = 0;
                        using var readBankCmd = conn.CreateCommand("SELECT BankName, AccountName, CurrentBalance FROM BankAccounts WHERE Id = @baId", tx);
                        readBankCmd.AddParam("baId", baId);
                        await using (var r = await readBankCmd.ExecuteReaderAsync())
                        {
                            if (await r.ReadAsync())
                            {
                                baName = $"{r.GetString(0)} - {r.GetString(1)}";
                                balAfter = r.GetDecimal(2);
                            }
                        }

                        var btSql = @"
                            INSERT INTO BankTransactions (BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, ReferenceId, ReferenceType, ""User"", CreatedAt)
                            VALUES (@baId, @baName, 'Cari Tahsilat', 'Giriş', @amt, 0, @bal, @desc, 'Banka / Havale', @refId, 'CARI', 'Yönetici', @created);";
                        await using var btCmd = conn.CreateCommand(btSql, tx);
                        btCmd.AddParam("baId", baId);
                        btCmd.AddParam("baName", baName);
                        btCmd.AddParam("amt", credit);
                        btCmd.AddParam("bal", balAfter);
                        btCmd.AddParam("desc", string.IsNullOrWhiteSpace(description) ? "Cari Tahsilat" : description);
                        btCmd.AddParam("refId", cariTxId);
                        btCmd.AddParam("created", date);
                        await btCmd.ExecuteNonQueryAsync();
                    }
                    else if (debit > 0 && (transactionType.Contains("Ödeme") || transactionType.Contains("Tediye")))
                    {
                        // Banka Çıkışı
                        var bAccSql = "UPDATE BankAccounts SET CurrentBalance = CurrentBalance - @amt, ActualBalance = ActualBalance - @amt WHERE Id = @baId";
                        await using var bAccCmd = conn.CreateCommand(bAccSql, tx);
                        bAccCmd.AddParam("amt", debit);
                        bAccCmd.AddParam("baId", baId);
                        await bAccCmd.ExecuteNonQueryAsync();

                        string baName = "Banka Hesabı";
                        decimal balAfter = 0;
                        using var readBankCmd = conn.CreateCommand("SELECT BankName, AccountName, CurrentBalance FROM BankAccounts WHERE Id = @baId", tx);
                        readBankCmd.AddParam("baId", baId);
                        await using (var r = await readBankCmd.ExecuteReaderAsync())
                        {
                            if (await r.ReadAsync())
                            {
                                baName = $"{r.GetString(0)} - {r.GetString(1)}";
                                balAfter = r.GetDecimal(2);
                            }
                        }

                        var btSql = @"
                            INSERT INTO BankTransactions (BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, ReferenceId, ReferenceType, ""User"", CreatedAt)
                            VALUES (@baId, @baName, 'Cari Ödeme', 'Çıkış', 0, @amt, @bal, @desc, 'Banka / Havale', @refId, 'CARI', 'Yönetici', @created);";
                        await using var btCmd = conn.CreateCommand(btSql, tx);
                        btCmd.AddParam("baId", baId);
                        btCmd.AddParam("baName", baName);
                        btCmd.AddParam("amt", debit);
                        btCmd.AddParam("bal", balAfter);
                        btCmd.AddParam("desc", string.IsNullOrWhiteSpace(description) ? "Cari Ödeme" : description);
                        btCmd.AddParam("refId", cariTxId);
                        btCmd.AddParam("created", date);
                        await btCmd.ExecuteNonQueryAsync();
                    }
                }

                await tx.CommitAsync();

                return new CariTransaction
                {
                    Id = cariTxId,
                    CariAccountId = cariAccountId,
                    CreatedAt = date,
                    TransactionType = transactionType,
                    DocumentNo = documentNo ?? "",
                    Debit = debit,
                    Credit = credit,
                    BalanceAfter = newBalance,
                    Description = description ?? "",
                    PaymentMethod = paymentMethod
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    #endregion

    #region Satış İşlem Motoru

    /// <summary>
    /// Tamamlanan bir satışın Nakit/Kredi Kartı/Veresiye entegrasyonunu
    /// tek bir merkezi akışla yürütür.
    /// </summary>
    public async Task ProcessSaleSettlementAsync(Sale sale)
    {
        if (sale.Id <= 0) return;

        bool dbConnected = await _dbService.TestConnectionAsync();
        if (!dbConnected) return;

        await using var conn = _dbService.CreateConnection();
        await conn.OpenAsync();

        // 1. Nakit Tutarı -> CashTransactions
        if (sale.CashAmount > 0)
        {
            var checkSql = "SELECT COUNT(*) FROM CashTransactions WHERE SourceDocumentType = 'SALE' AND SourceDocumentId = @saleId";
            using var checkCmd = conn.CreateCommand(checkSql);
            checkCmd.AddParam("saleId", sale.Id);
            int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (exists == 0)
            {
                var insCashSql = @"
                    INSERT INTO CashTransactions (
                        CashAccountId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId
                    )
                    VALUES (
                        1, 'Nakit Satış', @amt, @desc, @created, 'SALE', @saleId
                    );";
                await using var insCashCmd = conn.CreateCommand(insCashSql);
                insCashCmd.AddParam("amt", sale.CashAmount);
                insCashCmd.AddParam("desc", $"Satış #{sale.Id} Nakit Tahsilatı");
                insCashCmd.AddParam("created", sale.SaleDate);
                insCashCmd.AddParam("saleId", sale.Id);
                await insCashCmd.ExecuteNonQueryAsync();
            }
        }

        // 2. Kredi Kartı Tutarı -> PosTransactions
        if (sale.CardAmount > 0)
        {
            var checkPosSql = "SELECT COUNT(*) FROM PosTransactions WHERE SaleId = @saleId";
            using var checkPosCmd = conn.CreateCommand(checkPosSql);
            checkPosCmd.AddParam("saleId", sale.Id);
            int posExists = Convert.ToInt32(await checkPosCmd.ExecuteScalarAsync());

            if (posExists == 0)
            {
                var bankService = new BankService(_dbService);
                await bankService.RecordPosSaleAsync(sale.Id, sale.SaleDate, sale.CardAmount);
            }
        }

        // 3. Kasa hesabını merkezi olarak güncelle
        await GetCentralCashBalanceAsync();
    }

    #endregion
}
