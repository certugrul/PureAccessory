using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

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
                await using var conn = new SqlConnection(_dbService.ConnectionString);
                await conn.OpenAsync();

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
                    FROM CashTransactions;";

                await using var cmd = new SqlCommand(sql, conn);
                var result = await cmd.ExecuteScalarAsync();
                decimal balance = result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0m;

                // CashAccounts (Id = 1) tablosunu da anında güncel tut
                try
                {
                    var syncSql = @"
                        IF EXISTS (SELECT 1 FROM CashAccounts WHERE Id = 1)
                            UPDATE CashAccounts SET Balance = @bal WHERE Id = 1;
                        ELSE
                            INSERT INTO CashAccounts (Id, Name, Balance) VALUES (1, 'Merkez Kasa', @bal);";
                    await using var syncCmd = new SqlCommand(syncSql, conn);
                    syncCmd.Parameters.AddWithValue("@bal", balance);
                    await syncCmd.ExecuteNonQueryAsync();
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

            await using var conn = new SqlConnection(_dbService.ConnectionString);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

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

                    await using var insCmd = new SqlCommand(insSql, conn, tx);
                    insCmd.Parameters.AddWithValue("@no", invoice.InvoiceNo);
                    insCmd.Parameters.AddWithValue("@t", invoice.Type);
                    insCmd.Parameters.AddWithValue("@date", invoice.InvoiceDate);
                    insCmd.Parameters.AddWithValue("@soc", invoice.SupplierOrCustomer);
                    insCmd.Parameters.AddWithValue("@tax", (object?)invoice.TaxNo ?? DBNull.Value);
                    insCmd.Parameters.AddWithValue("@wid", invoice.WarehouseId);
                    insCmd.Parameters.AddWithValue("@wn", invoice.WarehouseName);
                    insCmd.Parameters.AddWithValue("@sub", invoice.SubTotal);
                    insCmd.Parameters.AddWithValue("@vat", invoice.VatTotal);
                    insCmd.Parameters.AddWithValue("@grand", invoice.GrandTotal);
                    insCmd.Parameters.AddWithValue("@ptype", invoice.PaymentType);
                    insCmd.Parameters.AddWithValue("@pstat", invoice.PaymentStatus);
                    insCmd.Parameters.AddWithValue("@baid", (object?)invoice.BankAccountId ?? DBNull.Value);
                    insCmd.Parameters.AddWithValue("@baname", (object?)invoice.BankAccountName ?? DBNull.Value);
                    insCmd.Parameters.AddWithValue("@caname", (object?)invoice.CurrentAccountName ?? DBNull.Value);
                    insCmd.Parameters.AddWithValue("@notes", (object?)invoice.Notes ?? DBNull.Value);
                    insCmd.Parameters.AddWithValue("@caid", (object?)invoice.CariAccountId ?? DBNull.Value);
                    insCmd.Parameters.AddWithValue("@paid", invoice.PaidAmount);
                    insCmd.Parameters.AddWithValue("@rem", invoice.RemainingAmount);

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

                    await using var updCmd = new SqlCommand(updSql, conn, tx);
                    updCmd.Parameters.AddWithValue("@id", invoice.Id);
                    updCmd.Parameters.AddWithValue("@no", invoice.InvoiceNo);
                    updCmd.Parameters.AddWithValue("@t", invoice.Type);
                    updCmd.Parameters.AddWithValue("@date", invoice.InvoiceDate);
                    updCmd.Parameters.AddWithValue("@soc", invoice.SupplierOrCustomer);
                    updCmd.Parameters.AddWithValue("@tax", (object?)invoice.TaxNo ?? DBNull.Value);
                    updCmd.Parameters.AddWithValue("@wid", invoice.WarehouseId);
                    updCmd.Parameters.AddWithValue("@wn", invoice.WarehouseName);
                    updCmd.Parameters.AddWithValue("@sub", invoice.SubTotal);
                    updCmd.Parameters.AddWithValue("@vat", invoice.VatTotal);
                    updCmd.Parameters.AddWithValue("@grand", invoice.GrandTotal);
                    updCmd.Parameters.AddWithValue("@ptype", invoice.PaymentType);
                    updCmd.Parameters.AddWithValue("@pstat", invoice.PaymentStatus);
                    updCmd.Parameters.AddWithValue("@baid", (object?)invoice.BankAccountId ?? DBNull.Value);
                    updCmd.Parameters.AddWithValue("@baname", (object?)invoice.BankAccountName ?? DBNull.Value);
                    updCmd.Parameters.AddWithValue("@caname", (object?)invoice.CurrentAccountName ?? DBNull.Value);
                    updCmd.Parameters.AddWithValue("@notes", (object?)invoice.Notes ?? DBNull.Value);
                    updCmd.Parameters.AddWithValue("@caid", (object?)invoice.CariAccountId ?? DBNull.Value);
                    updCmd.Parameters.AddWithValue("@paid", invoice.PaidAmount);
                    updCmd.Parameters.AddWithValue("@rem", invoice.RemainingAmount);
                    await updCmd.ExecuteNonQueryAsync();

                    // Eski kalemleri sil
                    var delItemsCmd = new SqlCommand("DELETE FROM InvoiceItems WHERE InvoiceId = @invId", conn, tx);
                    delItemsCmd.Parameters.AddWithValue("@invId", invoice.Id);
                    await delItemsCmd.ExecuteNonQueryAsync();
                }

                // 3. Fatura Kalemlerini Ekle
                foreach (var item in invoice.Items)
                {
                    item.InvoiceId = invoice.Id;
                    var itemSql = @"
                        INSERT INTO InvoiceItems (InvoiceId, ProductId, ProductCode, ProductName, Barcode, Quantity, UnitPrice, VatRate, LineTotal)
                        VALUES (@invId, @pid, @pcode, @pname, @barcode, @qty, @price, @vat, @total);";
                    await using var itemCmd = new SqlCommand(itemSql, conn, tx);
                    itemCmd.Parameters.AddWithValue("@invId", invoice.Id);
                    itemCmd.Parameters.AddWithValue("@pid", item.ProductId);
                    itemCmd.Parameters.AddWithValue("@pcode", (object?)item.ProductCode ?? DBNull.Value);
                    itemCmd.Parameters.AddWithValue("@pname", item.ProductName);
                    itemCmd.Parameters.AddWithValue("@barcode", (object?)item.Barcode ?? DBNull.Value);
                    itemCmd.Parameters.AddWithValue("@qty", item.Quantity);
                    itemCmd.Parameters.AddWithValue("@price", item.UnitPrice);
                    itemCmd.Parameters.AddWithValue("@vat", item.VatRate);
                    itemCmd.Parameters.AddWithValue("@total", item.LineTotal);
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
                        var stockSql = @"
                            IF EXISTS (SELECT 1 FROM WarehouseStocks WHERE WarehouseId = @wid AND ProductId = @pid)
                                UPDATE WarehouseStocks 
                                SET Quantity = Quantity + @delta 
                                WHERE WarehouseId = @wid AND ProductId = @pid;
                            ELSE
                                INSERT INTO WarehouseStocks (WarehouseId, ProductId, Quantity)
                                VALUES (@wid, @pid, @delta);";

                        await using var stockCmd = new SqlCommand(stockSql, conn, tx);
                        stockCmd.Parameters.AddWithValue("@wid", invoice.WarehouseId);
                        stockCmd.Parameters.AddWithValue("@pid", item.ProductId);
                        stockCmd.Parameters.AddWithValue("@delta", stockDelta);
                        await stockCmd.ExecuteNonQueryAsync();

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
                        await using var smCmd = new SqlCommand(smSql, conn, tx);
                        smCmd.Parameters.AddWithValue("@wid", invoice.WarehouseId);
                        smCmd.Parameters.AddWithValue("@pid", item.ProductId);
                        smCmd.Parameters.AddWithValue("@type", movType);
                        smCmd.Parameters.AddWithValue("@qty", item.Quantity);
                        smCmd.Parameters.AddWithValue("@desc", movDesc);
                        smCmd.Parameters.AddWithValue("@doc", invoice.InvoiceNo);
                        smCmd.Parameters.AddWithValue("@cat", isPurchase ? "Giriş" : "Çıkış");
                        smCmd.Parameters.AddWithValue("@created", invoice.InvoiceDate);
                        smCmd.Parameters.AddWithValue("@invId", invoice.Id);
                        await smCmd.ExecuteNonQueryAsync();

                        // Alış faturası ise ürün alış fiyatını güncelle
                        if (isPurchase && item.UnitPrice > 0)
                        {
                            var prodUpdSql = "UPDATE Products SET PurchasePrice = @pp WHERE Id = @pid";
                            await using var prodUpdCmd = new SqlCommand(prodUpdSql, conn, tx);
                            prodUpdCmd.Parameters.AddWithValue("@pp", item.UnitPrice);
                            prodUpdCmd.Parameters.AddWithValue("@pid", item.ProductId);
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
                        await using var cashCmd = new SqlCommand(cashSql, conn, tx);
                        cashCmd.Parameters.AddWithValue("@type", cashType);
                        cashCmd.Parameters.AddWithValue("@amt", invoice.GrandTotal);
                        cashCmd.Parameters.AddWithValue("@desc", $"{invoice.Type}: {invoice.SupplierOrCustomer} ({invoice.InvoiceNo})");
                        cashCmd.Parameters.AddWithValue("@created", invoice.InvoiceDate);
                        cashCmd.Parameters.AddWithValue("@invId", invoice.Id);
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
                            OUTPUT INSERTED.CurrentBalance, INSERTED.BankName + ' - ' + INSERTED.AccountName
                            WHERE Id = @baId;";
                        await using var bAccCmd = new SqlCommand(bAccSql, conn, tx);
                        bAccCmd.Parameters.AddWithValue("@delta", inflow - outflow);
                        bAccCmd.Parameters.AddWithValue("@baId", baId);

                        decimal balAfter = 0;
                        string baName = invoice.BankAccountName ?? "Banka Hesabı";
                        await using (var bReader = await bAccCmd.ExecuteReaderAsync())
                        {
                            if (await bReader.ReadAsync())
                            {
                                balAfter = bReader.GetDecimal(0);
                                baName = bReader.GetString(1);
                            }
                        }

                        var btSql = @"
                            INSERT INTO BankTransactions (
                                BankAccountId, BankAccountName, TransactionType, Direction,
                                Inflow, Outflow, BalanceAfter, Description, PaymentMethod,
                                ReferenceId, ReferenceType, [User], CreatedAt
                            )
                            VALUES (
                                @baId, @baName, @tt, @dir,
                                @inf, @outf, @bal, @desc, 'Banka / Havale',
                                @refId, 'FATURA', 'Yönetici', @created
                            );";
                        await using var btCmd = new SqlCommand(btSql, conn, tx);
                        btCmd.Parameters.AddWithValue("@baId", baId);
                        btCmd.Parameters.AddWithValue("@baName", baName);
                        btCmd.Parameters.AddWithValue("@tt", txType);
                        btCmd.Parameters.AddWithValue("@dir", direction);
                        btCmd.Parameters.AddWithValue("@inf", inflow);
                        btCmd.Parameters.AddWithValue("@outf", outflow);
                        btCmd.Parameters.AddWithValue("@bal", balAfter);
                        btCmd.Parameters.AddWithValue("@desc", $"{invoice.Type}: {invoice.SupplierOrCustomer} ({invoice.InvoiceNo})");
                        btCmd.Parameters.AddWithValue("@refId", invoice.Id);
                        btCmd.Parameters.AddWithValue("@created", invoice.InvoiceDate);
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
            await using var conn = new SqlConnection(_dbService.ConnectionString);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            try
            {
                // 1. Önceki hareketleri tersine çevir
                await RevertPreviousInvoiceMovementsInternalAsync(invoiceId, conn, tx);

                // 2. Kalemleri ve faturayı sil
                var delItemsCmd = new SqlCommand("DELETE FROM InvoiceItems WHERE InvoiceId = @invId", conn, tx);
                delItemsCmd.Parameters.AddWithValue("@invId", invoiceId);
                await delItemsCmd.ExecuteNonQueryAsync();

                var delInvCmd = new SqlCommand("DELETE FROM Invoices WHERE Id = @invId", conn, tx);
                delInvCmd.Parameters.AddWithValue("@invId", invoiceId);
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
    private static async Task RevertPreviousInvoiceMovementsInternalAsync(long invoiceId, SqlConnection conn, SqlTransaction tx)
    {
        // 1. Stok hareketlerini tersine çevir
        var getSmSql = "SELECT WarehouseId, ProductId, Type, Quantity FROM StockMovements WHERE SourceDocumentType = 'INVOICE' AND SourceDocumentId = @invId";
        var stockReverts = new List<(int WarehouseId, int ProductId, string Type, decimal Quantity)>();
        await using (var getSmCmd = new SqlCommand(getSmSql, conn, tx))
        {
            getSmCmd.Parameters.AddWithValue("@invId", invoiceId);
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
            var revStockCmd = new SqlCommand("UPDATE WarehouseStocks SET Quantity = Quantity + @delta WHERE WarehouseId = @wid AND ProductId = @pid", conn, tx);
            revStockCmd.Parameters.AddWithValue("@delta", reverseDelta);
            revStockCmd.Parameters.AddWithValue("@wid", sr.WarehouseId);
            revStockCmd.Parameters.AddWithValue("@pid", sr.ProductId);
            await revStockCmd.ExecuteNonQueryAsync();
        }

        var delSmCmd = new SqlCommand("DELETE FROM StockMovements WHERE SourceDocumentType = 'INVOICE' AND SourceDocumentId = @invId", conn, tx);
        delSmCmd.Parameters.AddWithValue("@invId", invoiceId);
        await delSmCmd.ExecuteNonQueryAsync();

        // 2. Cari hareketlerini temizle ve cari bakiyesini düzelt
        var getCtSql = "SELECT CariAccountId, Debit, Credit FROM CariTransactions WHERE InvoiceId = @invId";
        var cariReverts = new List<(int CariAccountId, decimal Debit, decimal Credit)>();
        await using (var getCtCmd = new SqlCommand(getCtSql, conn, tx))
        {
            getCtCmd.Parameters.AddWithValue("@invId", invoiceId);
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
            var revCariCmd = new SqlCommand("UPDATE CariAccounts SET Balance = Balance + @delta WHERE Id = @caId", conn, tx);
            revCariCmd.Parameters.AddWithValue("@delta", balDelta);
            revCariCmd.Parameters.AddWithValue("@caId", cr.CariAccountId);
            await revCariCmd.ExecuteNonQueryAsync();
        }

        var delCtCmd = new SqlCommand("DELETE FROM CariTransactions WHERE InvoiceId = @invId", conn, tx);
        delCtCmd.Parameters.AddWithValue("@invId", invoiceId);
        await delCtCmd.ExecuteNonQueryAsync();

        // 3. Kasa hareketini sil
        var delCashCmd = new SqlCommand("DELETE FROM CashTransactions WHERE SourceDocumentType = 'INVOICE' AND SourceDocumentId = @invId", conn, tx);
        delCashCmd.Parameters.AddWithValue("@invId", invoiceId);
        await delCashCmd.ExecuteNonQueryAsync();

        // 4. Banka hareketini tersine çevir ve sil
        var getBtSql = "SELECT BankAccountId, Inflow, Outflow FROM BankTransactions WHERE ReferenceType = 'FATURA' AND ReferenceId = @invId";
        var bankReverts = new List<(int BankAccountId, decimal Inflow, decimal Outflow)>();
        await using (var getBtCmd = new SqlCommand(getBtSql, conn, tx))
        {
            getBtCmd.Parameters.AddWithValue("@invId", invoiceId);
            await using var btReader = await getBtCmd.ExecuteReaderAsync();
            while (await btReader.ReadAsync())
            {
                bankReverts.Add((btReader.GetInt32(0), btReader.GetDecimal(1), btReader.GetDecimal(2)));
            }
        }

        foreach (var br in bankReverts)
        {
            decimal delta = -(br.Inflow - br.Outflow);
            var revBankCmd = new SqlCommand("UPDATE BankAccounts SET CurrentBalance = CurrentBalance + @delta, ActualBalance = ActualBalance + @delta WHERE Id = @baId", conn, tx);
            revBankCmd.Parameters.AddWithValue("@delta", delta);
            revBankCmd.Parameters.AddWithValue("@baId", br.BankAccountId);
            await revBankCmd.ExecuteNonQueryAsync();
        }

        var delBtCmd = new SqlCommand("DELETE FROM BankTransactions WHERE ReferenceType = 'FATURA' AND ReferenceId = @invId", conn, tx);
        delBtCmd.Parameters.AddWithValue("@invId", invoiceId);
        await delBtCmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertCariTransactionInternalAsync(
        SqlConnection conn,
        SqlTransaction tx,
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
        var updCariSql = @"
            UPDATE CariAccounts 
            SET Balance = Balance + @delta
            OUTPUT INSERTED.Balance
            WHERE Id = @id;";

        await using var updCmd = new SqlCommand(updCariSql, conn, tx);
        updCmd.Parameters.AddWithValue("@delta", debit - credit);
        updCmd.Parameters.AddWithValue("@id", cariAccountId);

        var balObj = await updCmd.ExecuteScalarAsync();
        decimal newBalance = balObj != null && balObj != DBNull.Value ? Convert.ToDecimal(balObj) : 0m;

        var insSql = @"
            INSERT INTO CariTransactions (
                CariAccountId, CreatedAt, TransactionType, DocumentNo, Debit, Credit, BalanceAfter, Description, InvoiceId, SaleId, PaymentMethod
            )
            VALUES (
                @caId, @created, @type, @doc, @deb, @cred, @bal, @desc, @invId, @saleId, @pm
            );";

        await using var insCmd = new SqlCommand(insSql, conn, tx);
        insCmd.Parameters.AddWithValue("@caId", cariAccountId);
        insCmd.Parameters.AddWithValue("@created", date);
        insCmd.Parameters.AddWithValue("@type", transactionType);
        insCmd.Parameters.AddWithValue("@doc", (object?)documentNo ?? DBNull.Value);
        insCmd.Parameters.AddWithValue("@deb", debit);
        insCmd.Parameters.AddWithValue("@cred", credit);
        insCmd.Parameters.AddWithValue("@bal", newBalance);
        insCmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        insCmd.Parameters.AddWithValue("@invId", (object?)invoiceId ?? DBNull.Value);
        insCmd.Parameters.AddWithValue("@saleId", (object?)saleId ?? DBNull.Value);
        insCmd.Parameters.AddWithValue("@pm", (object?)paymentMethod ?? DBNull.Value);
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
            await using var conn = new SqlConnection(_dbService.ConnectionString);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            try
            {
                // 1. Cari bakiyesini güncelle
                var updCariSql = @"
                    UPDATE CariAccounts 
                    SET Balance = Balance + @delta
                    OUTPUT INSERTED.Balance
                    WHERE Id = @id;";

                await using var updCmd = new SqlCommand(updCariSql, conn, tx);
                updCmd.Parameters.AddWithValue("@delta", debit - credit);
                updCmd.Parameters.AddWithValue("@id", cariAccountId);

                var balObj = await updCmd.ExecuteScalarAsync();
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

                await using var insCmd = new SqlCommand(insSql, conn, tx);
                insCmd.Parameters.AddWithValue("@caId", cariAccountId);
                insCmd.Parameters.AddWithValue("@created", date);
                insCmd.Parameters.AddWithValue("@type", transactionType);
                insCmd.Parameters.AddWithValue("@doc", (object?)documentNo ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@deb", debit);
                insCmd.Parameters.AddWithValue("@cred", credit);
                insCmd.Parameters.AddWithValue("@bal", newBalance);
                insCmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@pm", (object?)paymentMethod ?? DBNull.Value);

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
                        await using var cashCmd = new SqlCommand(cashSql, conn, tx);
                        cashCmd.Parameters.AddWithValue("@amt", credit);
                        cashCmd.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(description) ? "Cari Hesaptan Nakit Tahsilat" : description);
                        cashCmd.Parameters.AddWithValue("@created", date);
                        cashCmd.Parameters.AddWithValue("@cid", cariTxId);
                        await cashCmd.ExecuteNonQueryAsync();
                    }
                    else if (debit > 0 && (transactionType.Contains("Ödeme") || transactionType.Contains("Tediye")))
                    {
                        // Tedarikçiye nakit ödeme -> Kasa Çıkışı
                        var cashSql = @"
                            INSERT INTO CashTransactions (CashAccountId, Type, Amount, Description, CreatedAt, SourceDocumentType, SourceDocumentId)
                            VALUES (1, 'Nakit Ödeme (Cari)', @amt, @desc, @created, 'CARI', @cid);";
                        await using var cashCmd = new SqlCommand(cashSql, conn, tx);
                        cashCmd.Parameters.AddWithValue("@amt", debit);
                        cashCmd.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(description) ? "Cari Hesaba Nakit Ödeme" : description);
                        cashCmd.Parameters.AddWithValue("@created", date);
                        cashCmd.Parameters.AddWithValue("@cid", cariTxId);
                        await cashCmd.ExecuteNonQueryAsync();
                    }
                }
                else if (paymentMethod == "Banka Hesabı" && bankAccountId.HasValue && bankAccountId.Value > 0)
                {
                    int baId = bankAccountId.Value;
                    if (credit > 0)
                    {
                        // Banka Girişi
                        var bAccSql = "UPDATE BankAccounts SET CurrentBalance = CurrentBalance + @amt, ActualBalance = ActualBalance + @amt OUTPUT INSERTED.BankName + ' - ' + INSERTED.AccountName, INSERTED.CurrentBalance WHERE Id = @baId";
                        await using var bAccCmd = new SqlCommand(bAccSql, conn, tx);
                        bAccCmd.Parameters.AddWithValue("@amt", credit);
                        bAccCmd.Parameters.AddWithValue("@baId", baId);

                        string baName = "Banka Hesabı";
                        decimal balAfter = 0;
                        await using (var r = await bAccCmd.ExecuteReaderAsync())
                        {
                            if (await r.ReadAsync())
                            {
                                baName = r.GetString(0);
                                balAfter = r.GetDecimal(1);
                            }
                        }

                        var btSql = @"
                            INSERT INTO BankTransactions (BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, ReferenceId, ReferenceType, [User], CreatedAt)
                            VALUES (@baId, @baName, 'Cari Tahsilat', 'Giriş', @amt, 0, @bal, @desc, 'Banka / Havale', @refId, 'CARI', 'Yönetici', @created);";
                        await using var btCmd = new SqlCommand(btSql, conn, tx);
                        btCmd.Parameters.AddWithValue("@baId", baId);
                        btCmd.Parameters.AddWithValue("@baName", baName);
                        btCmd.Parameters.AddWithValue("@amt", credit);
                        btCmd.Parameters.AddWithValue("@bal", balAfter);
                        btCmd.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(description) ? "Cari Tahsilat" : description);
                        btCmd.Parameters.AddWithValue("@refId", cariTxId);
                        btCmd.Parameters.AddWithValue("@created", date);
                        await btCmd.ExecuteNonQueryAsync();
                    }
                    else if (debit > 0 && (transactionType.Contains("Ödeme") || transactionType.Contains("Tediye")))
                    {
                        // Banka Çıkışı
                        var bAccSql = "UPDATE BankAccounts SET CurrentBalance = CurrentBalance - @amt, ActualBalance = ActualBalance - @amt OUTPUT INSERTED.BankName + ' - ' + INSERTED.AccountName, INSERTED.CurrentBalance WHERE Id = @baId";
                        await using var bAccCmd = new SqlCommand(bAccSql, conn, tx);
                        bAccCmd.Parameters.AddWithValue("@amt", debit);
                        bAccCmd.Parameters.AddWithValue("@baId", baId);

                        string baName = "Banka Hesabı";
                        decimal balAfter = 0;
                        await using (var r = await bAccCmd.ExecuteReaderAsync())
                        {
                            if (await r.ReadAsync())
                            {
                                baName = r.GetString(0);
                                balAfter = r.GetDecimal(1);
                            }
                        }

                        var btSql = @"
                            INSERT INTO BankTransactions (BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, ReferenceId, ReferenceType, [User], CreatedAt)
                            VALUES (@baId, @baName, 'Cari Ödeme', 'Çıkış', 0, @amt, @bal, @desc, 'Banka / Havale', @refId, 'CARI', 'Yönetici', @created);";
                        await using var btCmd = new SqlCommand(btSql, conn, tx);
                        btCmd.Parameters.AddWithValue("@baId", baId);
                        btCmd.Parameters.AddWithValue("@baName", baName);
                        btCmd.Parameters.AddWithValue("@amt", debit);
                        btCmd.Parameters.AddWithValue("@bal", balAfter);
                        btCmd.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(description) ? "Cari Ödeme" : description);
                        btCmd.Parameters.AddWithValue("@refId", cariTxId);
                        btCmd.Parameters.AddWithValue("@created", date);
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

        await using var conn = new SqlConnection(_dbService.ConnectionString);
        await conn.OpenAsync();

        // 1. Nakit Tutarı -> CashTransactions
        if (sale.CashAmount > 0)
        {
            var checkSql = "SELECT COUNT(*) FROM CashTransactions WHERE SourceDocumentType = 'SALE' AND SourceDocumentId = @saleId";
            await using var checkCmd = new SqlCommand(checkSql, conn);
            checkCmd.Parameters.AddWithValue("@saleId", sale.Id);
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
                await using var insCashCmd = new SqlCommand(insCashSql, conn);
                insCashCmd.Parameters.AddWithValue("@amt", sale.CashAmount);
                insCashCmd.Parameters.AddWithValue("@desc", $"Satış #{sale.Id} Nakit Tahsilatı");
                insCashCmd.Parameters.AddWithValue("@created", sale.SaleDate);
                insCashCmd.Parameters.AddWithValue("@saleId", sale.Id);
                await insCashCmd.ExecuteNonQueryAsync();
            }
        }

        // 2. Kredi Kartı Tutarı -> PosTransactions
        if (sale.CardAmount > 0)
        {
            var checkPosSql = "SELECT COUNT(*) FROM PosTransactions WHERE SaleId = @saleId";
            await using var checkPosCmd = new SqlCommand(checkPosSql, conn);
            checkPosCmd.Parameters.AddWithValue("@saleId", sale.Id);
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
