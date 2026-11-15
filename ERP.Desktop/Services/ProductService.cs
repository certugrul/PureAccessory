using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;

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

        return new List<Product>();
        }
        finally
        {
            _productLock.Release();
        }
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

        InvalidateCache();
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
                InvalidateCache();
                return true;
            }
            catch { }
        }

        return false;
    }
}
