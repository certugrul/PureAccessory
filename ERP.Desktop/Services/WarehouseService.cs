using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class WarehouseService
{
    private readonly DatabaseService _dbService;
    private readonly ProductService _productService;

    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string WarehousesFile = Path.Combine(StorageDirectory, "warehouses.json");
    private static readonly string WarehouseStocksFile = Path.Combine(StorageDirectory, "warehouse_stocks.json");
    private static readonly string MovementsFile = Path.Combine(StorageDirectory, "stock_movements.json");

    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public WarehouseService(DatabaseService? dbService = null, ProductService? productService = null)
    {
        _dbService = dbService ?? new DatabaseService();
        _productService = productService ?? new ProductService(_dbService);
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

    public async Task<List<Warehouse>> GetAllWarehousesAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var list = await LoadWarehousesInternalAsync();
            if (list.Count == 0)
            {
                var defaultWarehouse = new Warehouse
                {
                    Id = 1,
                    Code = "DEP-01",
                    Name = "Ana Depo",
                    Location = "Merkez / Ana Bina",
                    Description = "İşletmenin ana stok deposu",
                    IsDefault = true,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                list.Add(defaultWarehouse);
                await SaveWarehousesInternalAsync(list);
            }
            return list.Where(w => w.IsActive).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Warehouse> GetDefaultWarehouseAsync()
    {
        var warehouses = await GetAllWarehousesAsync();
        return warehouses.FirstOrDefault(w => w.IsDefault) ?? warehouses.FirstOrDefault() ?? new Warehouse
        {
            Id = 1,
            Code = "DEP-01",
            Name = "Ana Depo",
            IsDefault = true
        };
    }

    public async Task<Warehouse> SaveOrUpdateWarehouseAsync(Warehouse warehouse)
    {
        await _semaphore.WaitAsync();
        try
        {
            var warehouses = await LoadWarehousesInternalAsync();
            if (warehouse.IsDefault)
            {
                // Diğer depoların varsayılan özelliğini kaldır
                foreach (var w in warehouses)
                {
                    w.IsDefault = false;
                }
            }

            if (warehouse.Id == 0)
            {
                warehouse.Id = warehouses.Count > 0 ? warehouses.Max(w => w.Id) + 1 : 1;
                if (string.IsNullOrWhiteSpace(warehouse.Code))
                {
                    warehouse.Code = $"DEP-{warehouse.Id:D2}";
                }
                warehouses.Add(warehouse);
            }
            else
            {
                var idx = warehouses.FindIndex(w => w.Id == warehouse.Id);
                if (idx >= 0)
                {
                    warehouses[idx] = warehouse;
                }
                else
                {
                    warehouses.Add(warehouse);
                }
            }

            await SaveWarehousesInternalAsync(warehouses);
            return warehouse;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteWarehouseAsync(int warehouseId)
    {
        await _semaphore.WaitAsync();
        try
        {
            var warehouses = await LoadWarehousesInternalAsync();
            var target = warehouses.FirstOrDefault(w => w.Id == warehouseId);
            if (target != null)
            {
                if (target.IsDefault)
                {
                    throw new InvalidOperationException("Varsayılan ana depo silinemez!");
                }
                target.IsActive = false;
                await SaveWarehousesInternalAsync(warehouses);
                return true;
            }
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<WarehouseStock>> GetWarehouseStocksAsync(int? warehouseId = null, string? searchTerm = null)
    {
        await EnsureWarehouseStocksInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var stocks = await LoadStocksInternalAsync();
            var warehouses = await LoadWarehousesInternalAsync();
            var products = await _productService.GetAllProductsAsync();

            bool modified = false;
            var defaultWh = warehouses.FirstOrDefault(w => w.IsDefault) ?? warehouses.FirstOrDefault();

            // Ürün fiyatlarını ve adlarını güncelle
            foreach (var stock in stocks)
            {
                var prod = products.FirstOrDefault(p => p.Id == stock.ProductId);
                if (prod != null)
                {
                    stock.ProductName = prod.Name;
                    stock.ProductCode = prod.Code;
                    stock.Barcode = prod.Barcode;
                    stock.PurchasePrice = prod.PurchasePrice;
                    stock.SalePrice = prod.SalePrice;
                }

                var wh = warehouses.FirstOrDefault(w => w.Id == stock.WarehouseId);
                if (wh != null)
                {
                    stock.WarehouseName = wh.Name;
                }
            }

            // Eğer depolarda henüz yer almayan ürün varsa, varsayılan depoya ekle
            foreach (var prod in products)
            {
                if (!stocks.Any(s => s.ProductId == prod.Id))
                {
                    stocks.Add(new WarehouseStock
                    {
                        Id = stocks.Count > 0 ? stocks.Max(s => s.Id) + 1 : 1,
                        WarehouseId = defaultWh?.Id ?? 1,
                        WarehouseName = defaultWh?.Name ?? "Ana Depo",
                        ProductId = prod.Id,
                        ProductCode = prod.Code,
                        ProductName = prod.Name,
                        Barcode = prod.Barcode,
                        Quantity = prod.Stock,
                        PurchasePrice = prod.PurchasePrice,
                        SalePrice = prod.SalePrice
                    });
                    modified = true;
                }
            }

            if (modified)
            {
                await SaveStocksInternalAsync(stocks);
            }

            var query = stocks.AsQueryable();

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                query = query.Where(s => s.WarehouseId == warehouseId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLowerInvariant();
                query = query.Where(s =>
                    s.ProductName.ToLowerInvariant().Contains(term) ||
                    s.ProductCode.ToLowerInvariant().Contains(term) ||
                    (!string.IsNullOrEmpty(s.Barcode) && s.Barcode.ToLowerInvariant().Contains(term)));
            }

            return query.OrderBy(s => s.WarehouseName).ThenBy(s => s.ProductName).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task AddStockAsync(int warehouseId, int productId, decimal quantity, string documentNo, string description, string movementType = "Alış Faturası Girişi")
    {
        if (quantity <= 0) return;

        await EnsureWarehouseStocksInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var warehouses = await LoadWarehousesInternalAsync();
            var wh = warehouses.FirstOrDefault(w => w.Id == warehouseId);
            var whName = wh?.Name ?? "Depo";

            var products = await _productService.GetAllProductsAsync();
            var product = products.FirstOrDefault(p => p.Id == productId);
            if (product == null) return;

            var stocks = await LoadStocksInternalAsync();
            var stockItem = stocks.FirstOrDefault(s => s.WarehouseId == warehouseId && s.ProductId == productId);

            if (stockItem == null)
            {
                stockItem = new WarehouseStock
                {
                    Id = stocks.Count > 0 ? stocks.Max(s => s.Id) + 1 : 1,
                    WarehouseId = warehouseId,
                    WarehouseName = whName,
                    ProductId = productId,
                    ProductCode = product.Code,
                    ProductName = product.Name,
                    Barcode = product.Barcode,
                    Quantity = quantity,
                    PurchasePrice = product.PurchasePrice,
                    SalePrice = product.SalePrice
                };
                stocks.Add(stockItem);
            }
            else
            {
                stockItem.Quantity += quantity;
                stockItem.PurchasePrice = product.PurchasePrice;
                stockItem.SalePrice = product.SalePrice;
            }

            await SaveStocksInternalAsync(stocks);

            // Stok hareketi kaydet
            await AppendMovementInternalAsync(new StockMovement
            {
                CreatedAt = DateTime.Now,
                WarehouseId = warehouseId,
                WarehouseName = whName,
                ProductId = productId,
                ProductCode = product.Code,
                ProductName = product.Name,
                MovementType = movementType,
                Quantity = quantity,
                DocumentNo = documentNo,
                Description = description
            });

            // Ürünün genel stoğunu da güncelle
            await _productService.UpdateStockAsync(productId, quantity);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DecreaseStockAsync(int warehouseId, int productId, decimal quantity, string documentNo, string description, string movementType = "Satış Çıkışı")
    {
        if (quantity <= 0) return;

        await EnsureWarehouseStocksInitializedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var warehouses = await LoadWarehousesInternalAsync();
            var wh = warehouses.FirstOrDefault(w => w.Id == warehouseId);
            var whName = wh?.Name ?? "Depo";

            var products = await _productService.GetAllProductsAsync();
            var product = products.FirstOrDefault(p => p.Id == productId);
            if (product == null) return;

            var stocks = await LoadStocksInternalAsync();
            var stockItem = stocks.FirstOrDefault(s => s.WarehouseId == warehouseId && s.ProductId == productId);

            if (stockItem == null)
            {
                stockItem = new WarehouseStock
                {
                    Id = stocks.Count > 0 ? stocks.Max(s => s.Id) + 1 : 1,
                    WarehouseId = warehouseId,
                    WarehouseName = whName,
                    ProductId = productId,
                    ProductCode = product.Code,
                    ProductName = product.Name,
                    Barcode = product.Barcode,
                    Quantity = -quantity,
                    PurchasePrice = product.PurchasePrice,
                    SalePrice = product.SalePrice
                };
                stocks.Add(stockItem);
            }
            else
            {
                stockItem.Quantity -= quantity;
            }

            await SaveStocksInternalAsync(stocks);

            // Stok hareketi kaydet
            await AppendMovementInternalAsync(new StockMovement
            {
                CreatedAt = DateTime.Now,
                WarehouseId = warehouseId,
                WarehouseName = whName,
                ProductId = productId,
                ProductCode = product.Code,
                ProductName = product.Name,
                MovementType = movementType,
                Quantity = -quantity,
                DocumentNo = documentNo,
                Description = description
            });

            // Ürünün genel stoğunu da düş
            await _productService.UpdateStockAsync(productId, -quantity);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task TransferStockAsync(int sourceWarehouseId, int targetWarehouseId, int productId, decimal quantity, string description)
    {
        if (sourceWarehouseId == targetWarehouseId)
        {
            throw new InvalidOperationException("Kaynak depo ile hedef depo aynı olamaz!");
        }

        if (quantity <= 0)
        {
            throw new InvalidOperationException("Transfer miktarı 0'dan büyük olmalıdır.");
        }

        var warehouses = await GetAllWarehousesAsync();
        var sourceWh = warehouses.FirstOrDefault(w => w.Id == sourceWarehouseId);
        var targetWh = warehouses.FirstOrDefault(w => w.Id == targetWarehouseId);

        if (sourceWh == null || targetWh == null)
        {
            throw new InvalidOperationException("Geçersiz depo seçimi.");
        }

        var docNo = $"TRF-{DateTime.Now:yyyyMMddHHmmss}";

        // Kaynaktan düş (ürünün genel stoğu değişmez, sadece depo stokları değişir)
        await _semaphore.WaitAsync();
        try
        {
            var products = await _productService.GetAllProductsAsync();
            var product = products.FirstOrDefault(p => p.Id == productId);
            if (product == null) throw new InvalidOperationException("Ürün bulunamadı.");

            var stocks = await LoadStocksInternalAsync();
            var sourceStock = stocks.FirstOrDefault(s => s.WarehouseId == sourceWarehouseId && s.ProductId == productId);
            if (sourceStock == null || sourceStock.Quantity < quantity)
            {
                throw new InvalidOperationException($"Kaynak depoda ({sourceWh.Name}) yeterli stok bulunmuyor! Mevcut Stok: {sourceStock?.Quantity ?? 0}");
            }

            sourceStock.Quantity -= quantity;

            var targetStock = stocks.FirstOrDefault(s => s.WarehouseId == targetWarehouseId && s.ProductId == productId);
            if (targetStock == null)
            {
                targetStock = new WarehouseStock
                {
                    Id = stocks.Count > 0 ? stocks.Max(s => s.Id) + 1 : 1,
                    WarehouseId = targetWarehouseId,
                    WarehouseName = targetWh.Name,
                    ProductId = productId,
                    ProductCode = product.Code,
                    ProductName = product.Name,
                    Barcode = product.Barcode,
                    Quantity = quantity,
                    PurchasePrice = product.PurchasePrice,
                    SalePrice = product.SalePrice
                };
                stocks.Add(targetStock);
            }
            else
            {
                targetStock.Quantity += quantity;
            }

            await SaveStocksInternalAsync(stocks);

            // Hareket kayıtları
            await AppendMovementInternalAsync(new StockMovement
            {
                CreatedAt = DateTime.Now,
                WarehouseId = sourceWarehouseId,
                WarehouseName = sourceWh.Name,
                ProductId = productId,
                ProductCode = product.Code,
                ProductName = product.Name,
                MovementType = "Depo Transferi (-)",
                Quantity = -quantity,
                DocumentNo = docNo,
                Description = $"{targetWh.Name} deposuna transfer. {description}".Trim()
            });

            await AppendMovementInternalAsync(new StockMovement
            {
                CreatedAt = DateTime.Now,
                WarehouseId = targetWarehouseId,
                WarehouseName = targetWh.Name,
                ProductId = productId,
                ProductCode = product.Code,
                ProductName = product.Name,
                MovementType = "Depo Transferi (+)",
                Quantity = quantity,
                DocumentNo = docNo,
                Description = $"{sourceWh.Name} deposundan transfer. {description}".Trim()
            });
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<StockMovement>> GetStockMovementsAsync(int? warehouseId = null, int? productId = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            var movements = await LoadMovementsInternalAsync();
            var query = movements.AsQueryable();

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                query = query.Where(m => m.WarehouseId == warehouseId.Value);
            }

            if (productId.HasValue && productId.Value > 0)
            {
                query = query.Where(m => m.ProductId == productId.Value);
            }

            return query.OrderByDescending(m => m.CreatedAt).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteStockMovementAsync(long movementId)
    {
        await _semaphore.WaitAsync();
        try
        {
            var movements = await LoadMovementsInternalAsync();
            var m = movements.FirstOrDefault(x => x.Id == movementId);
            if (m == null) return false;

            movements.Remove(m);
            var json = JsonSerializer.Serialize(movements, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(MovementsFile, json);
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ClearAllStockMovementsAsync(int? warehouseId = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                var movements = await LoadMovementsInternalAsync();
                movements.RemoveAll(m => m.WarehouseId == warehouseId.Value);
                var json = JsonSerializer.Serialize(movements, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(MovementsFile, json);
            }
            else
            {
                var empty = new List<StockMovement>();
                var json = JsonSerializer.Serialize(empty, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(MovementsFile, json);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task EnsureWarehouseStocksInitializedAsync()
    {
        if (File.Exists(WarehouseStocksFile)) return;

        var defaultWh = await GetDefaultWarehouseAsync();
        var products = await _productService.GetAllProductsAsync();

        var initialStocks = new List<WarehouseStock>();
        int id = 1;
        foreach (var p in products)
        {
            initialStocks.Add(new WarehouseStock
            {
                Id = id++,
                WarehouseId = defaultWh.Id,
                WarehouseName = defaultWh.Name,
                ProductId = p.Id,
                ProductCode = p.Code,
                ProductName = p.Name,
                Barcode = p.Barcode,
                Quantity = p.Stock,
                PurchasePrice = p.PurchasePrice,
                SalePrice = p.SalePrice
            });
        }

        await _semaphore.WaitAsync();
        try
        {
            await SaveStocksInternalAsync(initialStocks);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<List<Warehouse>> LoadWarehousesInternalAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<Warehouse>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = "SELECT Id, Code, Name, Location, Description, IsDefault, IsActive, CreatedAt FROM Warehouses WHERE IsActive = 1";
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new Warehouse
                    {
                        Id = reader.GetInt32(0),
                        Code = reader.GetString(1),
                        Name = reader.GetString(2),
                        Location = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        Description = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        IsDefault = reader.GetBoolean(5),
                        IsActive = reader.GetBoolean(6),
                        CreatedAt = reader.GetDateTime(7)
                    });
                }
                if (list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch { }

        if (!File.Exists(WarehousesFile)) return new List<Warehouse>();
        try
        {
            var json = await File.ReadAllTextAsync(WarehousesFile);
            return JsonSerializer.Deserialize<List<Warehouse>>(json) ?? new List<Warehouse>();
        }
        catch
        {
            return new List<Warehouse>();
        }
    }

    private async Task SaveWarehousesInternalAsync(List<Warehouse> list)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var w in list)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM Warehouses WHERE Id = @id");
                    checkCmd.AddParam("id", w.Id);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (exists)
                    {
                        var updateSql = @"UPDATE Warehouses SET Code=@code, Name=@name, Location=@loc, Description=@desc, IsDefault=@def, IsActive=@act WHERE Id=@id";
                        await using var cmd = conn.CreateCommand(updateSql);
                        cmd.AddParam("id", w.Id);
                        cmd.AddParam("code", w.Code);
                        cmd.AddParam("name", w.Name);
                        cmd.AddParam("loc", w.Location);
                        cmd.AddParam("desc", w.Description);
                        cmd.AddParam("def", w.IsDefault);
                        cmd.AddParam("act", w.IsActive);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var insertSql = @"INSERT INTO Warehouses (Id, Code, Name, Location, Description, IsDefault, IsActive, CreatedAt)
                                          VALUES (@id, @code, @name, @loc, @desc, @def, @act, @created);";
                        await using var cmd = conn.CreateCommand(insertSql);
                        cmd.AddParam("id", w.Id);
                        cmd.AddParam("code", w.Code);
                        cmd.AddParam("name", w.Name);
                        cmd.AddParam("loc", w.Location);
                        cmd.AddParam("desc", w.Description);
                        cmd.AddParam("def", w.IsDefault);
                        cmd.AddParam("act", w.IsActive);
                        cmd.AddParam("created", w.CreatedAt);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
        catch { }

        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(WarehousesFile, json);
    }

    private async Task<List<WarehouseStock>> LoadStocksInternalAsync()
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                var list = new List<WarehouseStock>();
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = "SELECT Id, WarehouseId, ProductId, Quantity FROM WarehouseStocks";
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new WarehouseStock
                    {
                        Id = reader.GetInt32(0),
                        WarehouseId = reader.GetInt32(1),
                        ProductId = reader.GetInt32(2),
                        Quantity = reader.GetDecimal(3)
                    });
                }
                if (list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch { }

        if (!File.Exists(WarehouseStocksFile)) return new List<WarehouseStock>();
        try
        {
            var json = await File.ReadAllTextAsync(WarehouseStocksFile);
            return JsonSerializer.Deserialize<List<WarehouseStock>>(json) ?? new List<WarehouseStock>();
        }
        catch
        {
            return new List<WarehouseStock>();
        }
    }

    private async Task SaveStocksInternalAsync(List<WarehouseStock> list)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                foreach (var s in list)
                {
                    using var checkCmd = conn.CreateCommand("SELECT COUNT(*) FROM WarehouseStocks WHERE WarehouseId = @wid AND ProductId = @pid");
                    checkCmd.AddParam("wid", s.WarehouseId);
                    checkCmd.AddParam("pid", s.ProductId);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (exists)
                    {
                        var updateSql = "UPDATE WarehouseStocks SET Quantity = @qty WHERE WarehouseId = @wid AND ProductId = @pid";
                        await using var cmd = conn.CreateCommand(updateSql);
                        cmd.AddParam("wid", s.WarehouseId);
                        cmd.AddParam("pid", s.ProductId);
                        cmd.AddParam("qty", s.Quantity);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var insertSql = "INSERT INTO WarehouseStocks (WarehouseId, ProductId, Quantity) VALUES (@wid, @pid, @qty)";
                        await using var cmd = conn.CreateCommand(insertSql);
                        cmd.AddParam("wid", s.WarehouseId);
                        cmd.AddParam("pid", s.ProductId);
                        cmd.AddParam("qty", s.Quantity);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
        catch { }

        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(WarehouseStocksFile, json);
    }

    private static async Task<List<StockMovement>> LoadMovementsInternalAsync()
    {
        if (!File.Exists(MovementsFile)) return new List<StockMovement>();
        try
        {
            var json = await File.ReadAllTextAsync(MovementsFile);
            return JsonSerializer.Deserialize<List<StockMovement>>(json) ?? new List<StockMovement>();
        }
        catch
        {
            return new List<StockMovement>();
        }
    }

    private async Task AppendMovementInternalAsync(StockMovement movement)
    {
        try
        {
            if (await _dbService.TestConnectionAsync())
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();
                var sql = @"INSERT INTO StockMovements (WarehouseId, ProductId, Type, Quantity, Description, DocumentNo, Category, CreatedAt)
                            VALUES (@wid, @pid, @type, @qty, @desc, @doc, @cat, @created)";
                await using var cmd = conn.CreateCommand(sql);
                cmd.AddParam("wid", movement.WarehouseId);
                cmd.AddParam("pid", movement.ProductId);
                cmd.AddParam("type", movement.MovementType);
                cmd.AddParam("qty", movement.Quantity);
                cmd.AddParam("desc", movement.Description);
                cmd.AddParam("doc", movement.DocumentNo);
                cmd.AddParam("cat", DBNull.Value);
                cmd.AddParam("created", movement.CreatedAt);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        catch { }

        var list = await LoadMovementsInternalAsync();
        movement.Id = list.Count > 0 ? list.Max(m => m.Id) + 1 : 1;
        list.Add(movement);
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(MovementsFile, json);
    }
}
