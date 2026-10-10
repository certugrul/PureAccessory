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

    public async Task<List<Product>> GetAllProductsAsync()
    {
        // 1. Önce SQL Server'dan çekmeyi dene
        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var conn = new SqlConnection(_dbService.ConnectionString);
                await conn.OpenAsync();
                var sql = "SELECT Id, Code, Name, Barcode, PurchasePrice, SalePrice, VatRate FROM Products WHERE IsActive = 1";
                await using var cmd = new SqlCommand(sql, conn);
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
                        Stock = 0
                    });
                }

                if (dbList.Count > 0)
                {
                    await SyncWithWarehouseStocksInternalAsync(dbList);
                    return dbList;
                }
            }
            catch { }
        }

        // 2. SQL'de yoksa veya bağlı değilse yerel JSON'dan oku
        var localList = await LoadFromLocalJsonAsync();
        if (localList.Count == 0)
        {
            // İlk kez açılıyorsa örnek hazır ürünler oluştur
            localList = GetDefaultProducts();
            await SaveAllToLocalJsonAsync(localList);
        }

        await SyncWithWarehouseStocksInternalAsync(localList);
        return localList;
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
                var totalWhStock = stocks.Where(s => s.ProductId == productId).Sum(s => s.Quantity);
                if (item != null)
                {
                    item.Stock = totalWhStock;
                    await SaveAllToLocalJsonAsync(products);
                }
                return;
            }
            catch { }
        }

        // Depo dosyası yoksa doğrudan ürün stoğunu güncelle
        if (item != null)
        {
            item.Stock += quantityChange;
            if (item.Stock < 0) item.Stock = 0;
            await SaveAllToLocalJsonAsync(products);
        }
    }

    public async Task<Product> SaveOrUpdateProductAsync(Product product)
    {
        var products = await GetAllProductsAsync();

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

        // SQL Server bağlıysa orada da güncelle/ekle
        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var conn = new SqlConnection(_dbService.ConnectionString);
                await conn.OpenAsync();

                var mergeSql = @"
                    IF EXISTS (SELECT 1 FROM Products WHERE Id = @Id)
                    BEGIN
                        UPDATE Products 
                        SET Code = @Code, Name = @Name, Barcode = @Barcode, 
                            PurchasePrice = @PurchasePrice, SalePrice = @SalePrice, 
                            VatRate = @VatRate, IsActive = 1
                        WHERE Id = @Id
                    END
                    ELSE
                    BEGIN
                        INSERT INTO Products (Code, Name, Barcode, PurchasePrice, SalePrice, VatRate, IsActive)
                        VALUES (@Code, @Name, @Barcode, @PurchasePrice, @SalePrice, @VatRate, 1)
                    END";

                await using var cmd = new SqlCommand(mergeSql, conn);
                cmd.Parameters.AddWithValue("@Id", product.Id);
                cmd.Parameters.AddWithValue("@Code", product.Code);
                cmd.Parameters.AddWithValue("@Name", product.Name);
                cmd.Parameters.AddWithValue("@Barcode", (object?)product.Barcode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PurchasePrice", product.PurchasePrice);
                cmd.Parameters.AddWithValue("@SalePrice", product.SalePrice);
                cmd.Parameters.AddWithValue("@VatRate", product.VatRate);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

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

            // SQL Server bağlıysa pasife al
            bool dbConnected = await _dbService.TestConnectionAsync();
            if (dbConnected)
            {
                try
                {
                    await using var conn = new SqlConnection(_dbService.ConnectionString);
                    await conn.OpenAsync();
                    var sql = "UPDATE Products SET IsActive = 0 WHERE Id = @Id";
                    await using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Id", productId);
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
                Code = "UR-001",
                Name = "Coca Cola 330ml Kutu",
                Barcode = "8690504011234",
                QrCode = "QR-KOLA-330",
                PurchasePrice = 20.00m,
                SalePrice = 35.00m,
                VatRate = 20,
                Stock = 120
            },
            new Product
            {
                Id = 2,
                Code = "UR-002",
                Name = "Tam Buğday Ekmeği",
                Barcode = "8690000000002",
                QrCode = "QR-EKMEK-01",
                PurchasePrice = 10.00m,
                SalePrice = 15.00m,
                VatRate = 1,
                Stock = 85
            },
            new Product
            {
                Id = 3,
                Code = "UR-003",
                Name = "Doğal Kaynak Suyu 0.5L",
                Barcode = "8690000000003",
                QrCode = "QR-SU-05",
                PurchasePrice = 5.00m,
                SalePrice = 10.00m,
                VatRate = 10,
                Stock = 250
            },
            new Product
            {
                Id = 4,
                Code = "UR-004",
                Name = "Filtre Kahve 250g",
                Barcode = "8690000000004",
                QrCode = "QR-KAHVE-250",
                PurchasePrice = 120.00m,
                SalePrice = 180.00m,
                VatRate = 20,
                Stock = 40
            },
            new Product
            {
                Id = 5,
                Code = "UR-005",
                Name = "Çikolatalı Gofret 40g",
                Barcode = "8690000000005",
                QrCode = "QR-GOFRET-01",
                PurchasePrice = 8.00m,
                SalePrice = 15.00m,
                VatRate = 20,
                Stock = 180
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
