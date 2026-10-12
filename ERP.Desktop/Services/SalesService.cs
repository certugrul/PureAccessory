using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class SalesService
{
    private readonly DatabaseService _dbService;
    private readonly ProductService _productService;
    private readonly WarehouseService _warehouseService;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string StorageFile = Path.Combine(StorageDirectory, "sales.json");

    public SalesService(DatabaseService? dbService = null, ProductService? productService = null, WarehouseService? warehouseService = null)
    {
        _dbService = dbService ?? new DatabaseService();
        _productService = productService ?? new ProductService(_dbService);
        _warehouseService = warehouseService ?? new WarehouseService(_dbService, _productService);
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

    private static readonly SemaphoreSlim _saleLock = new(1, 1);
    private static string? _lastSaleFingerprint;
    private static DateTime _lastSaleCompletedTime = DateTime.MinValue;
    private static Sale? _lastCompletedSale;

    public async Task<Sale> CompleteSaleAsync(
        IEnumerable<CartItem> cartItems, 
        string paymentType, 
        DateTime? customDate = null, 
        decimal receiptDiscount = 0,
        decimal cashAmount = 0,
        decimal cardAmount = 0)
    {
        var itemsList = cartItems.ToList();
        if (itemsList.Count == 0)
        {
            throw new InvalidOperationException("Sepet boş, satış yapılamaz.");
        }

        // Idempotency fingerprint
        var currentFingerprint = string.Join(";", itemsList.OrderBy(x => x.ProductId).Select(x => $"{x.ProductId}:{x.Quantity}:{x.UnitPrice}:{x.DiscountAmount}"))
            + $"|{paymentType}|{receiptDiscount:F2}|{cashAmount:F2}|{cardAmount:F2}";

        await _saleLock.WaitAsync();
        try
        {
            // Mükerrer satış çağrısı kontrolü: 3.5 saniye içinde aynı sepet tekrar gelirse mevcut satışı dön
            if (_lastSaleFingerprint == currentFingerprint && 
                (DateTime.Now - _lastSaleCompletedTime).TotalSeconds < 3.5 && 
                _lastCompletedSale != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SalesService] Mükerrer satış çağrısı engellendi. Mevcut Fiş #{_lastCompletedSale.Id} dönülüyor.");
                return _lastCompletedSale;
            }

        var saleDate = customDate ?? DateTime.Now;

        // Kasa açılışı kontrolü: Gün açılmadan satış yapılamaz
        var cashService = new CashService(_dbService, this);
        var session = await cashService.GetTodaySessionAsync(saleDate) ?? await cashService.GetActiveSessionAsync();
        if (session == null || session.Status != "Açık")
        {
            throw new InvalidOperationException("Kasa kapalı! Satış yapabilmek için önce Gün Açılışı (Kasa Açılışı) yapılmalıdır.");
        }

        var sale = new Sale
        {
            SaleDate = saleDate,
            PaymentType = paymentType
        };

        decimal grossTotal = itemsList.Sum(c => c.GrossTotal);
        decimal lineDiscountTotal = itemsList.Sum(c => c.DiscountAmount);
        decimal itemsNetTotal = itemsList.Sum(c => c.Total);
        if (receiptDiscount > itemsNetTotal) receiptDiscount = itemsNetTotal;

        decimal receiptRatio = itemsNetTotal > 0 ? (receiptDiscount / itemsNetTotal) : 0m;

        decimal subTotal = 0;
        decimal vatTotal = 0;
        decimal costTotal = 0;

        var defaultWh = await _warehouseService.GetDefaultWarehouseAsync();

        foreach (var c in itemsList)
        {
            var netLineTotal = Math.Max(0, c.Total * (1m - receiptRatio));
            var lineCost = c.Quantity * c.UnitCost;

            // KDV hesaplama
            var vatFactor = c.VatRate / (100m + c.VatRate);
            var itemVat = netLineTotal * vatFactor;

            subTotal += (netLineTotal - itemVat);
            vatTotal += itemVat;
            costTotal += lineCost;

            sale.Items.Add(new SaleItem
            {
                ProductId = c.ProductId,
                ProductName = c.Name,
                ProductCode = c.Code,
                Quantity = c.Quantity,
                UnitPrice = c.UnitPrice,
                DiscountAmount = c.DiscountAmount,
                VatRate = c.VatRate,
                UnitCost = c.UnitCost
            });

            // Seçilen depodan ve ürün stoğundan düş, hareket kaydı oluştur
            await _warehouseService.DecreaseStockAsync(defaultWh.Id, c.ProductId, c.Quantity, $"Fiş (Satış)", "Hızlı Satış Çıkışı", "Satış Çıkışı");
        }

        sale.GrossTotal = Math.Round(grossTotal, 2);
        sale.LineDiscountTotal = Math.Round(lineDiscountTotal, 2);
        sale.ReceiptDiscount = Math.Round(receiptDiscount, 2);
        sale.SubTotal = Math.Round(subTotal, 2);
        sale.VatTotal = Math.Round(vatTotal, 2);
        sale.GrandTotal = Math.Round(Math.Max(0, itemsNetTotal - receiptDiscount), 2);
        sale.CostTotal = Math.Round(costTotal, 2);

        // Nakit ve Kredi Kartı tutarlarını ayarla
        if (paymentType == "Nakit")
        {
            sale.CashAmount = sale.GrandTotal;
            sale.CardAmount = 0m;
        }
        else if (paymentType == "Kredi Kartı" || paymentType == "Kart")
        {
            sale.CashAmount = 0m;
            sale.CardAmount = sale.GrandTotal;
        }
        else if (paymentType == "Parçalı")
        {
            sale.CashAmount = Math.Round(cashAmount, 2);
            sale.CardAmount = Math.Round(cardAmount, 2);
        }
        else
        {
            sale.CashAmount = 0m;
            sale.CardAmount = 0m;
        }

        // 1. Yerel JSON'a kalıcı olarak ekle
        await SaveSaleToLocalJsonAsync(sale);

        // 2. SQL Server bağlıysa veritabanına ekle (Çevrimiçi modda coordinator gerçek SaleId ile oluşturur)
        bool dbConnected = await _dbService.TestConnectionAsync();
        if (!dbConnected && sale.CardAmount > 0)
        {
            try
            {
                var bankService = new BankService(_dbService);
                await bankService.RecordPosSaleAsync(sale.Id, sale.SaleDate, sale.CardAmount);
            }
            catch { }
        }

        if (dbConnected)
        {
            try
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                var saleSql = @"
                    INSERT INTO Sales (WarehouseId, SaleDate, SubTotal, VatTotal, GrandTotal, CostTotal, PaymentType, DiscountTotal, CashAmount, CardAmount)
                    VALUES (@WarehouseId, @SaleDate, @SubTotal, @VatTotal, @GrandTotal, @CostTotal, @PaymentType, @DiscountTotal, @CashAmount, @CardAmount);
                    SELECT SCOPE_IDENTITY();";

                await using var cmd = conn.CreateCommand(saleSql);
                cmd.AddParam("WarehouseId", defaultWh.Id);
                cmd.AddParam("SaleDate", sale.SaleDate);
                cmd.AddParam("SubTotal", sale.SubTotal);
                cmd.AddParam("VatTotal", sale.VatTotal);
                cmd.AddParam("GrandTotal", sale.GrandTotal);
                cmd.AddParam("CostTotal", sale.CostTotal);
                cmd.AddParam("PaymentType", sale.PaymentType);
                cmd.AddParam("DiscountTotal", sale.DiscountTotal);
                cmd.AddParam("CashAmount", sale.CashAmount);
                cmd.AddParam("CardAmount", sale.CardAmount);

                var generatedId = await cmd.ExecuteScalarAsync();
                if (generatedId != null && generatedId != DBNull.Value)
                {
                    sale.Id = Convert.ToInt64(generatedId);

                    foreach (var item in sale.Items)
                    {
                        var itemSql = @"
                            INSERT INTO SaleItems (SaleId, ProductId, Quantity, UnitPrice, VatRate, UnitCost, DiscountAmount)
                            VALUES (@SaleId, @ProductId, @Quantity, @UnitPrice, @VatRate, @UnitCost, @DiscountAmount);";

                        await using var itemCmd = conn.CreateCommand(itemSql);
                        itemCmd.AddParam("SaleId", sale.Id);
                        itemCmd.AddParam("ProductId", item.ProductId);
                        itemCmd.AddParam("Quantity", item.Quantity);
                        itemCmd.AddParam("UnitPrice", item.UnitPrice);
                        itemCmd.AddParam("VatRate", item.VatRate);
                        itemCmd.AddParam("UnitCost", item.UnitCost);
                        itemCmd.AddParam("DiscountAmount", item.DiscountAmount);
                        await itemCmd.ExecuteNonQueryAsync();
                    }

                    // Merkezi finans ve kasa/POS entegrasyonu
                    var coordinator = new FinancialTransactionCoordinator(_dbService);
                    await coordinator.ProcessSaleSettlementAsync(sale);

                    // Veresiye ise ve müşteri seçiliyse Cari hareket oluştur
                    if (sale.PaymentType == "Veresiye" && sale.CustomerId.HasValue && sale.CustomerId.Value > 0)
                    {
                        var cariService = new CariService(_dbService);
                        await cariService.AddTransactionAsync(
                            cariAccountId: sale.CustomerId.Value,
                            transactionType: "Veresiye Satış",
                            debit: sale.GrandTotal,
                            credit: 0,
                            documentNo: $"Satış #{sale.Id}",
                            description: $"Satış #{sale.Id} Veresiye Borcu",
                            customDate: sale.SaleDate
                        );
                    }
                }
            }
            catch { }
        }

        _lastSaleFingerprint = currentFingerprint;
        _lastSaleCompletedTime = DateTime.Now;
        _lastCompletedSale = sale;

        return sale;
        }
        finally
        {
            _saleLock.Release();
        }
    }

    public async Task<List<Sale>> GetSalesForDateAsync(DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);

        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var sales = new List<Sale>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                var saleSql = @"SELECT Id, WarehouseId, SaleDate, SubTotal, VatTotal, GrandTotal, CostTotal, PaymentType,
                                       COALESCE(CashAmount, 0), COALESCE(CardAmount, 0)
                                FROM Sales 
                                WHERE SaleDate >= @StartDate AND SaleDate < @EndDate
                                ORDER BY SaleDate DESC, Id DESC";

                await using (var cmd = conn.CreateCommand(saleSql))
                {
                    cmd.AddParam("StartDate", start);
                    cmd.AddParam("EndDate", end);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var grandTotal = reader.GetDecimal(5);
                        var ptype = reader.GetString(7);
                        var cAmount = reader.GetDecimal(8);
                        var cardAmt = reader.GetDecimal(9);

                        if (cAmount == 0 && cardAmt == 0)
                        {
                            if (ptype == "Nakit") cAmount = grandTotal;
                            else if (ptype == "Kredi Kartı" || ptype == "Kart") cardAmt = grandTotal;
                        }

                        sales.Add(new Sale
                        {
                            Id = reader.GetInt64(0),
                            SaleDate = reader.GetDateTime(2),
                            SubTotal = reader.GetDecimal(3),
                            VatTotal = reader.GetDecimal(4),
                            GrandTotal = grandTotal,
                            CostTotal = reader.GetDecimal(6),
                            PaymentType = ptype,
                            CashAmount = cAmount,
                            CardAmount = cardAmt,
                            Items = new List<SaleItem>()
                        });
                    }
                }

                if (sales.Count > 0)
                {
                    var saleIds = string.Join(",", sales.Select(s => s.Id));
                    var itemsSql = $@"SELECT si.Id, si.SaleId, si.ProductId, si.Quantity, si.UnitPrice, si.VatRate, si.UnitCost,
                                            COALESCE(p.Name, 'Ürün') AS ProductName,
                                            COALESCE(p.Code, '') AS ProductCode
                                     FROM SaleItems si
                                     LEFT JOIN Products p ON si.ProductId = p.Id
                                     WHERE si.SaleId IN ({saleIds})";

                    await using var itemCmd = conn.CreateCommand(itemsSql);
                    await using var itemReader = await itemCmd.ExecuteReaderAsync();

                    var itemsBySale = new Dictionary<long, List<SaleItem>>();
                    while (await itemReader.ReadAsync())
                    {
                        long saleId = itemReader.GetInt64(1);
                        var item = new SaleItem
                        {
                            Id = itemReader.GetInt64(0),
                            SaleId = saleId,
                            ProductId = itemReader.GetInt32(2),
                            Quantity = itemReader.GetDecimal(3),
                            UnitPrice = itemReader.GetDecimal(4),
                            VatRate = itemReader.GetDecimal(5),
                            UnitCost = itemReader.GetDecimal(6),
                            ProductName = itemReader.GetString(7),
                            ProductCode = itemReader.GetString(8)
                        };

                        if (!itemsBySale.TryGetValue(saleId, out var itemList))
                        {
                            itemList = new List<SaleItem>();
                            itemsBySale[saleId] = itemList;
                        }
                        itemList.Add(item);
                    }

                    foreach (var s in sales)
                    {
                        if (itemsBySale.TryGetValue(s.Id, out var sItems))
                        {
                            s.Items = sItems;
                        }
                    }
                }

                return sales;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SalesService] GetSalesForDateAsync hatası: {ex.Message}");
        }

        var localSales = await LoadSalesFromLocalJsonAsync();
        return localSales.Where(s => s.SaleDate.Date == date.Date).ToList();
    }

    public async Task<DailyReceiptBreakdownData> GetDailyReceiptBreakdownAsync(DateTime date)
    {
        var daySales = (await GetSalesForDateAsync(date))
            .OrderByDescending(s => s.SaleDate)
            .ToList();

        var result = new DailyReceiptBreakdownData
        {
            TargetDate = date.Date,
            TotalSalesCount = daySales.Count,
            TotalRevenue = daySales.Sum(s => s.GrandTotal),
            CashRevenue = daySales.Sum(s => s.GetEffectiveCashAmount()),
            CreditCardRevenue = daySales.Sum(s => s.GetEffectiveCardAmount()),
            OnAccountRevenue = daySales.Sum(s => s.GetEffectiveOnAccountAmount())
        };

        result.Receipts = daySales.Select(s => new DailyReceiptRow
        {
            SaleId = s.Id,
            SaleDate = s.SaleDate,
            PaymentType = s.PaymentType,
            CashAmount = s.GetEffectiveCashAmount(),
            CardAmount = s.GetEffectiveCardAmount(),
            SubTotal = s.SubTotal,
            VatTotal = s.VatTotal,
            GrandTotal = s.GrandTotal,
            TotalQuantity = s.Items.Sum(i => i.Quantity),
            ItemCount = s.Items.Count,
            Items = s.Items
        }).ToList();

        result.TotalQuantitySold = result.Receipts.Sum(r => r.TotalQuantity);

        var flatItems = daySales.SelectMany(s => s.Items).ToList();
        var grouped = flatItems
            .GroupBy(i => new { i.ProductId, i.ProductCode, i.ProductName })
            .OrderByDescending(g => g.Sum(x => x.LineTotal))
            .ToList();

        result.TotalDistinctProducts = grouped.Count;

        foreach (var g in grouped)
        {
            var totalQty = g.Sum(x => x.Quantity);
            var totalRev = g.Sum(x => x.LineTotal);
            var avgPrice = totalQty > 0 ? totalRev / totalQty : (g.FirstOrDefault()?.UnitPrice ?? 0m);
            var vatTot = g.Sum(x => x.LineTotal * (x.VatRate / (100m + x.VatRate)));
            var distinctReceipts = daySales.Count(s => s.Items.Any(i => i.ProductId == g.Key.ProductId));
            var share = result.TotalRevenue > 0 ? (totalRev / result.TotalRevenue) * 100m : 0m;

            result.SoldProducts.Add(new DailySoldProductSummary
            {
                ProductId = g.Key.ProductId,
                ProductCode = g.Key.ProductCode,
                ProductName = g.Key.ProductName,
                TotalQuantity = totalQty,
                UnitPrice = Math.Round(avgPrice, 2),
                VatTotal = Math.Round(vatTot, 2),
                TotalRevenue = Math.Round(totalRev, 2),
                ReceiptCount = distinctReceipts,
                RevenueSharePercent = Math.Round(share, 1)
            });
        }

        return result;
    }

    public async Task<List<Sale>> GetAllSalesAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var sales = new List<Sale>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                var saleSql = @"SELECT Id, WarehouseId, SaleDate, SubTotal, VatTotal, GrandTotal, CostTotal, PaymentType,
                                       COALESCE(CashAmount, 0), COALESCE(CardAmount, 0)
                                FROM Sales 
                                ORDER BY SaleDate DESC, Id DESC";
                await using (var cmd = conn.CreateCommand(saleSql))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var grandTotal = reader.GetDecimal(5);
                        var ptype = reader.GetString(7);
                        var cAmount = reader.GetDecimal(8);
                        var cardAmt = reader.GetDecimal(9);

                        if (cAmount == 0 && cardAmt == 0)
                        {
                            if (ptype == "Nakit") cAmount = grandTotal;
                            else if (ptype == "Kredi Kartı" || ptype == "Kart") cardAmt = grandTotal;
                        }

                        sales.Add(new Sale
                        {
                            Id = reader.GetInt64(0),
                            SaleDate = reader.GetDateTime(2),
                            SubTotal = reader.GetDecimal(3),
                            VatTotal = reader.GetDecimal(4),
                            GrandTotal = grandTotal,
                            CostTotal = reader.GetDecimal(6),
                            PaymentType = ptype,
                            CashAmount = cAmount,
                            CardAmount = cardAmt,
                            Items = new List<SaleItem>()
                        });
                    }
                }

                if (sales.Count > 0)
                {
                    var itemsSql = @"SELECT si.Id, si.SaleId, si.ProductId, si.Quantity, si.UnitPrice, si.VatRate, si.UnitCost,
                                            COALESCE(p.Name, 'Ürün') AS ProductName,
                                            COALESCE(p.Code, '') AS ProductCode
                                     FROM SaleItems si
                                     LEFT JOIN Products p ON si.ProductId = p.Id";
                    await using var itemCmd = conn.CreateCommand(itemsSql);
                    await using var itemReader = await itemCmd.ExecuteReaderAsync();

                    var itemsBySale = new Dictionary<long, List<SaleItem>>();
                    while (await itemReader.ReadAsync())
                    {
                        long saleId = itemReader.GetInt64(1);
                        var item = new SaleItem
                        {
                            Id = itemReader.GetInt64(0),
                            SaleId = saleId,
                            ProductId = itemReader.GetInt32(2),
                            Quantity = itemReader.GetDecimal(3),
                            UnitPrice = itemReader.GetDecimal(4),
                            VatRate = itemReader.GetDecimal(5),
                            UnitCost = itemReader.GetDecimal(6),
                            ProductName = itemReader.GetString(7),
                            ProductCode = itemReader.GetString(8)
                        };

                        if (!itemsBySale.TryGetValue(saleId, out var itemList))
                        {
                            itemList = new List<SaleItem>();
                            itemsBySale[saleId] = itemList;
                        }
                        itemList.Add(item);
                    }

                    foreach (var s in sales)
                    {
                        if (itemsBySale.TryGetValue(s.Id, out var sItems))
                        {
                            s.Items = sItems;
                        }
                    }

                    return sales;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SalesService] SQL Server satışları okunamadı: {ex.Message}");
        }

        return await LoadSalesFromLocalJsonAsync();
    }

    private static async Task SaveSaleToLocalJsonAsync(Sale sale)
    {
        try
        {
            EnsureStorageDirectory();
            var list = await LoadSalesFromLocalJsonAsync();
            if (sale.Id == 0)
            {
                sale.Id = list.Count > 0 ? list.Max(x => x.Id) + 1 : 1;
            }
            list.Insert(0, sale);

            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(StorageFile, json);
        }
        catch { }
    }

    private static async Task<List<Sale>> LoadSalesFromLocalJsonAsync()
    {
        try
        {
            if (File.Exists(StorageFile))
            {
                var json = await File.ReadAllTextAsync(StorageFile);
                var list = JsonSerializer.Deserialize<List<Sale>>(json);
                if (list != null) return list;
            }
        }
        catch { }

        return new List<Sale>();
    }

    /// <summary>
    /// Fişi/satışı kalıcı olarak iptal eder ve siler:
    /// 1. Satıştaki ürün adetlerini depoya ve stoklara geri iade eder.
    /// 2. Kasa ve POS/banka finansal kayıtlarını temizler/dengeler.
    /// 3. Satış ve satış kalemlerini hem SQL Server'dan hem yerel JSON'dan siler.
    /// </summary>
    public async Task<bool> DeleteSaleAsync(long saleId)
    {
        try
        {
            var allSales = await GetAllSalesAsync();
            var targetSale = allSales.FirstOrDefault(s => s.Id == saleId);
            if (targetSale == null)
            {
                throw new InvalidOperationException($"#{saleId} numaralı satış/fiş kaydı bulunamadı.");
            }

            var defaultWh = await _warehouseService.GetDefaultWarehouseAsync();

            // 1. Stokları depoya geri iade et
            foreach (var item in targetSale.Items)
            {
                try
                {
                    await _warehouseService.AddStockAsync(
                        defaultWh.Id, 
                        item.ProductId, 
                        item.Quantity, 
                        $"Fiş İptali #{saleId}", 
                        "Satış İptali Stok İadesi", 
                        "Stok Girişi"
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SalesService] Stok iade hatası: {ex.Message}");
                }
            }

            // 2. Veritabanı bağlıysa veritabanından temizle
            if (await _dbService.TestConnectionAsync())
            {
                try
                {
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();

                    // Kasa hareketlerini sil (bu satışla ilgili olanlar)
                    var delCashSql = "DELETE FROM CashTransactions WHERE Description LIKE @Pattern";
                    await using (var cmdCash = conn.CreateCommand(delCashSql))
                    {
                        cmdCash.AddParam("Pattern", $"%Satış #{saleId}%");
                        await cmdCash.ExecuteNonQueryAsync();
                    }

                    // POS hareketlerini sil
                    var delPosSql = "DELETE FROM PosTransactions WHERE Description LIKE @Pattern";
                    await using (var cmdPos = conn.CreateCommand(delPosSql))
                    {
                        cmdPos.AddParam("Pattern", $"%Satış #{saleId}%");
                        await cmdPos.ExecuteNonQueryAsync();
                    }

                    // Cari hareketleri sil (veresiye ise)
                    var delCariSql = "DELETE FROM CariTransactions WHERE DocumentNo = @DocNo";
                    await using (var cmdCari = conn.CreateCommand(delCariSql))
                    {
                        cmdCari.AddParam("DocNo", $"Satış #{saleId}");
                        await cmdCari.ExecuteNonQueryAsync();
                    }

                    // Satış Kalemlerini sil
                    var delItemsSql = "DELETE FROM SaleItems WHERE SaleId = @SaleId";
                    await using (var cmdItems = conn.CreateCommand(delItemsSql))
                    {
                        cmdItems.AddParam("SaleId", saleId);
                        await cmdItems.ExecuteNonQueryAsync();
                    }

                    // Satışı sil
                    var delSaleSql = "DELETE FROM Sales WHERE Id = @SaleId";
                    await using (var cmdSale = conn.CreateCommand(delSaleSql))
                    {
                        cmdSale.AddParam("SaleId", saleId);
                        await cmdSale.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SalesService] DB satış silme hatası: {ex.Message}");
                }
            }

            // 3. Yerel JSON'dan sil
            try
            {
                if (File.Exists(StorageFile))
                {
                    var list = await LoadSalesFromLocalJsonAsync();
                    list.RemoveAll(s => s.Id == saleId);
                    var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(StorageFile, json);
                }
            }
            catch { }

            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Fiş silinirken hata oluştu: {ex.Message}", ex);
        }
    }
}
