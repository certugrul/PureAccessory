using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class ProductService
{
    private readonly DatabaseService _dbService;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string StorageFile = Path.Combine(StorageDirectory, "products.json");
    private static readonly string WarehouseStocksFile = Path.Combine(StorageDirectory, "warehouse_stocks.json");

    private static List<Product>? _cachedProducts;
    private static DateTime _lastCacheTime = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(3);
    private static readonly SemaphoreSlim _productLock = new(1, 1);

    public static void InvalidateCache()
    {
        _cachedProducts = null;
        _lastCacheTime = DateTime.MinValue;
    }

    public ProductService(DatabaseService? dbService = null)
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

    public async Task<List<Product>> GetAllProductsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedProducts != null && (DateTime.Now - _lastCacheTime) < CacheTtl)
        {
            return _cachedProducts.ToList();
        }

        await _productLock.WaitAsync();
        try
        {
            if (!forceRefresh && _cachedProducts != null && (DateTime.Now - _lastCacheTime) < CacheTtl)
            {
                return _cachedProducts.ToList();
            }

            // 1. Önce SQL Server'dan depodaki gerçek stoklarıyla birlikte çek
        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = @"
                    SELECT p.Id, p.Code, p.Name, p.Barcode, p.PurchasePrice, p.SalePrice, p.VatRate,
                           ISNULL(ws.TotalStock, 0) AS TotalStock
                    FROM Products p
                    LEFT JOIN (
                        SELECT ProductId, SUM(Quantity) AS TotalStock
                        FROM WarehouseStocks
                        GROUP BY ProductId
                    ) ws ON p.Id = ws.ProductId
                    WHERE p.IsActive = 1
                    ORDER BY p.Id ASC";

                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();

                var dbList = new List<Product>();
                while (await reader.ReadAsync())
                {
                    var barcode = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    dbList.Add(new Product
                    {
                        Id = reader.GetInt32(0),
                        Code = reader.GetString(1),
                        Name = reader.GetString(2),
                        Barcode = barcode,
                        QrCode = $"QR-{reader.GetString(1)}",
                        PurchasePrice = reader.GetDecimal(4),
                        SalePrice = reader.GetDecimal(5),
                        VatRate = reader.GetDecimal(6),
                        Stock = reader.GetDecimal(7)
                    });
                }

                if (dbList.Count > 0)
                {
                    // Yerel önbelleğe senkronize et
                    await SaveAllToLocalJsonAsync(dbList);
                    _cachedProducts = dbList;
                    _lastCacheTime = DateTime.Now;
                    return dbList.ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProductService] DB Ürün sorgulama hatası: {ex.Message}");
            }
        }

        // 2. SQL'e ulaşılamadıysa yerel JSON'dan oku
        var localList = await LoadFromLocalJsonAsync();
        if (localList.Count > 0)
        {
            await SyncWithWarehouseStocksInternalAsync(localList);
            _cachedProducts = localList;
            _lastCacheTime = DateTime.Now;
            return localList.ToList();
        }

        // 3. Veritabanına ulaşılamadıysa ve yerel kayıt yoksa uyarı için boş liste dön
        return new List<Product>();
        }
        finally
        {
            _productLock.Release();
        }
    }

    private async Task SyncWithWarehouseStocksInternalAsync(List<Product> products)
    {
        try
        {
            if (!File.Exists(WarehouseStocksFile))
            {
                var initialStocks = products.Select((p, idx) => new WarehouseStock
                {
                    Id = idx + 1,
                    WarehouseId = 1,
                    WarehouseName = "Ana Depo",
                    ProductId = p.Id,
                    ProductCode = p.Code,
                    ProductName = p.Name,
                    Barcode = p.Barcode,
                    Quantity = p.Stock,
                    PurchasePrice = p.PurchasePrice,
                    SalePrice = p.SalePrice
                }).ToList();

                var json = JsonSerializer.Serialize(initialStocks, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(WarehouseStocksFile, json);
                return;
            }

            var stocksJson = await File.ReadAllTextAsync(WarehouseStocksFile);
            var stocks = JsonSerializer.Deserialize<List<WarehouseStock>>(stocksJson) ?? new List<WarehouseStock>();

            bool stocksModified = false;
            bool productsModified = false;

            foreach (var p in products)
            {
                var matchingStocks = stocks.Where(s => s.ProductId == p.Id).ToList();
                if (matchingStocks.Count > 0)
                {
                    var totalStock = matchingStocks.Sum(s => s.Quantity);
                    if (p.Stock != totalStock)
                    {
                        p.Stock = totalStock;
                        productsModified = true;
                    }

                    // En güncel alış fiyatını yansıt
                    var latestPrice = matchingStocks.First().PurchasePrice;
                    if (p.PurchasePrice != latestPrice && latestPrice > 0)
                    {
                        p.PurchasePrice = latestPrice;
                        productsModified = true;
                    }
                }
                else
                {
                    // Ürün depolarda henüz kayıtlı değilse varsayılan depoya ekle
                    stocks.Add(new WarehouseStock
                    {
                        Id = stocks.Count > 0 ? stocks.Max(s => s.Id) + 1 : 1,
                        WarehouseId = 1,
                        WarehouseName = "Ana Depo",
                        ProductId = p.Id,
                        ProductCode = p.Code,
                        ProductName = p.Name,
                        Barcode = p.Barcode,
                        Quantity = p.Stock,
                        PurchasePrice = p.PurchasePrice,
                        SalePrice = p.SalePrice
                    });
                    stocksModified = true;
                }
            }

            if (stocksModified)
            {
                var json = JsonSerializer.Serialize(stocks, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(WarehouseStocksFile, json);
            }

            if (productsModified)
            {
                await SaveAllToLocalJsonAsync(products);
            }
        }
        catch { }
    }

    public async Task<Product?> FindByScanAsync(string scanInput)
    {
        if (string.IsNullOrWhiteSpace(scanInput)) return null;

        var term = scanInput.Trim();

        // QR kod prefixleri temizleme (örneğin "QR:KOLA-01" veya "ERP:UR-001")
        if (term.StartsWith("QR:", StringComparison.OrdinalIgnoreCase))
        {
            term = term[3..].Trim();
        }
        else if (term.StartsWith("ERP:", StringComparison.OrdinalIgnoreCase))
        {
            term = term[4..].Trim();
        }

        var products = await GetAllProductsAsync();

        // 1. Tam QR Kod eşleşmesi
        var match = products.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.QrCode) && p.QrCode.Equals(term, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        // 2. Barkod eşleşmesi
        match = products.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.Barcode) && p.Barcode.Equals(term, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        // 3. Ürün Kodu eşleşmesi
        match = products.FirstOrDefault(p =>
            p.Code.Equals(term, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        // 4. İsim içinde geçen eşleşme (eğer tam kod tutmadıysa)
        return products.FirstOrDefault(p =>
            p.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpdateStockAsync(int productId, decimal quantityChange)
    {
        var products = await LoadFromLocalJsonAsync();
        var item = products.FirstOrDefault(p => p.Id == productId);

        // Depo stokları mevcutsa, ürünün genel stoğunu depolardaki toplam miktara senkronize et
        if (File.Exists(WarehouseStocksFile))
        {
            try
            {
                var stocksJson = await File.ReadAllTextAsync(WarehouseStocksFile);
                var stocks = JsonSerializer.Deserialize<List<WarehouseStock>>(stocksJson) ?? new List<WarehouseStock>();
                var targetStock = stocks.FirstOrDefault(s => s.ProductId == productId && s.WarehouseId == 1) 
                               ?? stocks.FirstOrDefault(s => s.ProductId == productId);
                if (targetStock != null)
                {
                    targetStock.Quantity += quantityChange;
                    if (targetStock.Quantity < 0) targetStock.Quantity = 0;
                }
                var totalWhStock = stocks.Where(s => s.ProductId == productId).Sum(s => s.Quantity);
                if (item != null)
                {
                    item.Stock = totalWhStock;
                    await SaveAllToLocalJsonAsync(products);
                }
                var json = JsonSerializer.Serialize(stocks, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(WarehouseStocksFile, json);
            }
            catch { }
        }
        else if (item != null)
        {
            item.Stock += quantityChange;
            if (item.Stock < 0) item.Stock = 0;
            await SaveAllToLocalJsonAsync(products);
        }

        // Veritabanında anında WarehouseStocks tablosunu güncelle
        if (await _dbService.TestConnectionAsync())
        {
            try
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var updateStockSql = @"
                    UPDATE WarehouseStocks 
                    SET Quantity = CASE WHEN Quantity + @Change < 0 THEN 0 ELSE Quantity + @Change END 
                    WHERE ProductId = @ProductId AND WarehouseId = 1;";
                await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(updateStockSql));
                cmd.AddParam("Change", quantityChange);
                cmd.AddParam("ProductId", productId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProductService] DB Stok güncelleme hatası: {ex.Message}");
            }
        }

        InvalidateCache();
    }

    public async Task<Product> SaveOrUpdateProductAsync(Product product)
    {
        // 1. Veritabanı bağlıysa önce doğrudan DB'ye yaz ve gerçek Id'yi al
        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                if (product.Id > 0)
                {
                    using var checkCmd = conn.CreateCommand(_dbService.NormalizeSql("SELECT COUNT(*) FROM Products WHERE Id = @Id"));
                    checkCmd.AddParam("Id", product.Id);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (exists)
                    {
                        var updateSql = @"
                            UPDATE Products 
                            SET Code = @Code, Name = @Name, Barcode = @Barcode, 
                                PurchasePrice = @PurchasePrice, SalePrice = @SalePrice, 
                                VatRate = @VatRate, IsActive = 1
                            WHERE Id = @Id";
                        await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(updateSql));
                        cmd.AddParam("Id", product.Id);
                        cmd.AddParam("Code", product.Code);
                        cmd.AddParam("Name", product.Name);
                        cmd.AddParam("Barcode", product.Barcode);
                        cmd.AddParam("PurchasePrice", product.PurchasePrice);
                        cmd.AddParam("SalePrice", product.SalePrice);
                        cmd.AddParam("VatRate", product.VatRate);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var insertSql = @"
                            INSERT INTO Products (Code, Name, Barcode, PurchasePrice, SalePrice, VatRate, IsActive)
                            VALUES (@Code, @Name, @Barcode, @PurchasePrice, @SalePrice, @VatRate, 1);
                            SELECT SCOPE_IDENTITY();";
                        await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(insertSql));
                        cmd.AddParam("Code", product.Code);
                        cmd.AddParam("Name", product.Name);
                        cmd.AddParam("Barcode", product.Barcode);
                        cmd.AddParam("PurchasePrice", product.PurchasePrice);
                        cmd.AddParam("SalePrice", product.SalePrice);
                        cmd.AddParam("VatRate", product.VatRate);
                        var scalar = await cmd.ExecuteScalarAsync();
                        if (scalar != null && scalar != DBNull.Value)
                        {
                            product.Id = Convert.ToInt32(scalar);
                        }
                    }
                }
                else
                {
                    var insertSql = @"
                        INSERT INTO Products (Code, Name, Barcode, PurchasePrice, SalePrice, VatRate, IsActive)
                        VALUES (@Code, @Name, @Barcode, @PurchasePrice, @SalePrice, @VatRate, 1);
                        SELECT SCOPE_IDENTITY();";
                    await using var cmd = conn.CreateCommand(_dbService.NormalizeSql(insertSql));
                    cmd.AddParam("Code", product.Code);
                    cmd.AddParam("Name", product.Name);
                    cmd.AddParam("Barcode", product.Barcode);
                    cmd.AddParam("PurchasePrice", product.PurchasePrice);
                    cmd.AddParam("SalePrice", product.SalePrice);
                    cmd.AddParam("VatRate", product.VatRate);
                    var scalar = await cmd.ExecuteScalarAsync();
                    if (scalar != null && scalar != DBNull.Value)
                    {
                        product.Id = Convert.ToInt32(scalar);
                    }
                }

                // WarehouseStocks tablosundaki stoğu doğrudan veritabanında da kaydet / güncelle
                if (product.Id > 0)
                {
                    var updStockSql = @"
                        UPDATE WarehouseStocks 
                        SET Quantity = @Stock, PurchasePrice = @PurchasePrice, SalePrice = @SalePrice 
                        WHERE ProductId = @ProductId AND WarehouseId = 1";
                    await using var stockCmd = conn.CreateCommand(_dbService.NormalizeSql(updStockSql));
                    stockCmd.AddParam("Stock", product.Stock);
                    stockCmd.AddParam("PurchasePrice", product.PurchasePrice);
                    stockCmd.AddParam("SalePrice", product.SalePrice);
                    stockCmd.AddParam("ProductId", product.Id);
                    int rowsAffected = await stockCmd.ExecuteNonQueryAsync();

                    if (rowsAffected == 0)
                    {
                        var insStockSql = @"
                            INSERT INTO WarehouseStocks (WarehouseId, ProductId, Quantity, PurchasePrice, SalePrice)
                            VALUES (1, @ProductId, @Stock, @PurchasePrice, @SalePrice)";
                        await using var insCmd = conn.CreateCommand(_dbService.NormalizeSql(insStockSql));
                        insCmd.AddParam("ProductId", product.Id);
                        insCmd.AddParam("Stock", product.Stock);
                        insCmd.AddParam("PurchasePrice", product.PurchasePrice);
                        insCmd.AddParam("SalePrice", product.SalePrice);
                        await insCmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProductService] Ürün kaydetme DB hatası: {ex.Message}");
            }
        }

        // 2. Yerel JSON dosyasını senkronize et
        var products = await LoadFromLocalJsonAsync();
        if (product.Id == 0)
        {
            product.Id = products.Count > 0 ? products.Max(p => p.Id) + 1 : 1;
            products.Add(product);
        }
        else
        {
            var existingIndex = products.FindIndex(p => p.Id == product.Id);
            if (existingIndex >= 0)
            {
                products[existingIndex] = product;
            }
            else
            {
                products.Add(product);
            }
        }
        await SaveAllToLocalJsonAsync(products);

        // Depo stoklarını da senkronize et
        try
        {
            if (File.Exists(WarehouseStocksFile))
            {
                var stocksJson = await File.ReadAllTextAsync(WarehouseStocksFile);
                var stocks = JsonSerializer.Deserialize<List<WarehouseStock>>(stocksJson) ?? new List<WarehouseStock>();

                var matchingStocks = stocks.Where(s => s.ProductId == product.Id).ToList();
                if (matchingStocks.Count > 0)
                {
                    foreach (var s in matchingStocks)
                    {
                        s.ProductName = product.Name;
                        s.ProductCode = product.Code;
                        s.Barcode = product.Barcode;
                        s.PurchasePrice = product.PurchasePrice;
                        s.SalePrice = product.SalePrice;
                    }

                    var currentTotalWhStock = matchingStocks.Sum(s => s.Quantity);
                    var diff = product.Stock - currentTotalWhStock;
                    if (diff != 0)
                    {
                        var mainWhStock = matchingStocks.FirstOrDefault(s => s.WarehouseId == 1) ?? matchingStocks.First();
                        mainWhStock.Quantity += diff;
                    }
                }
                else
                {
                    stocks.Add(new WarehouseStock
                    {
                        Id = stocks.Count > 0 ? stocks.Max(s => s.Id) + 1 : 1,
                        WarehouseId = 1,
                        WarehouseName = "Ana Depo",
                        ProductId = product.Id,
                        ProductCode = product.Code,
                        ProductName = product.Name,
                        Barcode = product.Barcode,
                        Quantity = product.Stock,
                        PurchasePrice = product.PurchasePrice,
                        SalePrice = product.SalePrice
                    });
                }

                var json = JsonSerializer.Serialize(stocks, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(WarehouseStocksFile, json);
            }
        }
        catch { }

        InvalidateCache();
        AppDataCache.UpdateProducts(products);
        return product;
    }

    public async Task<bool> UpdateProductPriceAsync(int productId, decimal newSalePrice)
    {
        var products = await GetAllProductsAsync();
        var item = products.FirstOrDefault(p => p.Id == productId);
        if (item == null) return false;
        item.SalePrice = newSalePrice;
        await SaveOrUpdateProductAsync(item);
        return true;
    }


    public async Task<bool> DeleteProductAsync(int productId)
    {
        var products = await GetAllProductsAsync();
        var item = products.FirstOrDefault(p => p.Id == productId);
        if (item != null)
        {
            products.Remove(item);
            await SaveAllToLocalJsonAsync(products);

            try
            {
                if (File.Exists(WarehouseStocksFile))
                {
                    var stocksJson = await File.ReadAllTextAsync(WarehouseStocksFile);
                    var stocks = JsonSerializer.Deserialize<List<WarehouseStock>>(stocksJson) ?? new List<WarehouseStock>();
                    stocks.RemoveAll(s => s.ProductId == productId);
                    var json = JsonSerializer.Serialize(stocks, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(WarehouseStocksFile, json);
                }
            }
            catch { }
            InvalidateCache();

            // Veritabanı bağlıysa pasife al
            bool dbConnected = await _dbService.TestConnectionAsync();
            if (dbConnected)
            {
                try
                {
                    await using var conn = _dbService.CreateConnection();
                    await conn.OpenAsync();
                    var sql = "UPDATE Products SET IsActive = 0 WHERE Id = @Id";
                    await using var cmd = conn.CreateCommand(sql);
                    cmd.AddParam("Id", productId);
                    await cmd.ExecuteNonQueryAsync();
                }
                catch { }
            }

            return true;
        }

        return false;
    }

    private static List<Product> GetDefaultProducts()
    {
        return new List<Product>
        {
            new Product
            {
                Id = 1,
                Code = "PURE001",
                Name = "Çelik Kolye",
                Barcode = "0000",
                QrCode = "QR-PURE001",
                PurchasePrice = 100.00m,
                SalePrice = 210.00m,
                VatRate = 20,
                Stock = 269
            },
            new Product
            {
                Id = 2,
                Code = "PURE002",
                Name = "Çelik Küpe",
                Barcode = "0002",
                QrCode = "QR-PURE002",
                PurchasePrice = 90.00m,
                SalePrice = 200.00m,
                VatRate = 20,
                Stock = 130
            },
            new Product
            {
                Id = 3,
                Code = "PURE003",
                Name = "Çelik Bilezik",
                Barcode = "8694506266231",
                QrCode = "QR-PURE003",
                PurchasePrice = 95.00m,
                SalePrice = 200.00m,
                VatRate = 20,
                Stock = 97
            },
            new Product
            {
                Id = 4,
                Code = "PURE004",
                Name = "AÇIK ÜRÜN",
                Barcode = "",
                QrCode = "QR-PURE004",
                PurchasePrice = 0m,
                SalePrice = 0m,
                VatRate = 20,
                Stock = 0
            }
        };
    }

    private static async Task<List<Product>> LoadFromLocalJsonAsync()
    {
        try
        {
            if (File.Exists(StorageFile))
            {
                var json = await File.ReadAllTextAsync(StorageFile);
                var list = JsonSerializer.Deserialize<List<Product>>(json);
                if (list != null) return list;
            }
        }
        catch { }

        return new List<Product>();
    }

    public static async Task SaveAllToLocalJsonAsync(List<Product> products)
    {
        try
        {
            EnsureStorageDirectory();
            var json = JsonSerializer.Serialize(products, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(StorageFile, json);
        }
        catch { }
    }
}
