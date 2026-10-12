using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class BranchSyncService
{
    private readonly DatabaseService _dbService;
    private readonly SalesService _salesService;
    private readonly CashService _cashService;
    private readonly ClosureService _closureService;

    public BranchSyncService(DatabaseService? dbService = null)
    {
        _dbService = dbService ?? new DatabaseService();
        _salesService = new SalesService(_dbService);
        _cashService = new CashService(_dbService, _salesService);
        _closureService = new ClosureService(_dbService);
    }

    /// <summary>
    /// Belirtilen tarih aralığında mağazada gerçekleşen tüm satış, kasa ve ciro verilerini paketler.
    /// </summary>
    public async Task<BranchSyncPackage> CreateSyncPackageAsync(DateTime startDate, DateTime endDate, string branchName = "Pure Accessory Mağaza")
    {
        var start = startDate.Date;
        var end = endDate.Date.AddDays(1).AddTicks(-1);

        var package = new BranchSyncPackage
        {
            BranchName = branchName,
            StartDate = start,
            EndDate = end,
            CreatedAt = DateTime.Now
        };

        // 1. Satışları ve Satış Kalemlerini Getir (Hedeflenen tarih aralığını doğrudan DB'den çek)
        package.Sales = (await _salesService.GetSalesBetweenDatesAsync(start, end))
            .OrderBy(s => s.SaleDate)
            .ToList();

        // 2. Kasa Hareketlerini Getir
        if (await _dbService.TestConnectionAsync())
        {
            try
            {
                await using var conn = new SqlConnection(_dbService.ConnectionString);
                await conn.OpenAsync();

                // Kasa Hareketleri
                var txSql = @"SELECT Id, SessionId, Type, Amount, Description, CreatedAt 
                              FROM CashTransactions 
                              WHERE CreatedAt >= @Start AND CreatedAt <= @End 
                              ORDER BY CreatedAt ASC";
                await using (var cmd = new SqlCommand(txSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Start", start);
                    cmd.Parameters.AddWithValue("@End", end);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        package.CashTransactions.Add(new CashTransaction
                        {
                            Id = reader.GetInt64(0),
                            SessionId = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                            Type = reader.GetString(2),
                            Amount = reader.GetDecimal(3),
                            Description = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            CreatedAt = reader.GetDateTime(5)
                        });
                    }
                }

                // Kasa Oturumları
                var sesSql = @"SELECT Id, SessionDate, OpenedAt, ClosedAt, OpeningCash, Status, Notes 
                               FROM CashSessions 
                               WHERE SessionDate >= @Start AND SessionDate <= @End 
                               ORDER BY SessionDate ASC";
                await using (var cmd = new SqlCommand(sesSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Start", start);
                    cmd.Parameters.AddWithValue("@End", end);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        package.CashSessions.Add(new CashSession
                        {
                            Id = reader.GetInt64(0),
                            SessionDate = reader.GetDateTime(1),
                            OpenedAt = reader.GetDateTime(2),
                            ClosedAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                            OpeningCash = reader.GetDecimal(4),
                            Status = reader.GetString(5),
                            Notes = reader.IsDBNull(6) ? null : reader.GetString(6)
                        });
                    }
                }
            }
            catch { }
        }

        // 3. Gün Sonu Kapanışlarını Getir
        var closures = await _closureService.GetClosuresAsync();
        package.Closures = closures
            .Where(c => c.ClosureDate >= start && c.ClosureDate <= end)
            .OrderBy(c => c.ClosureDate)
            .ToList();

        // 4. Özet Finansal Metrikleri Hesapla
        package.SalesCount = package.Sales.Count;
        package.TotalRevenue = package.Sales.Sum(s => s.GrandTotal);
        package.TotalCash = package.Sales.Sum(s => s.GetEffectiveCashAmount());
        package.TotalCard = package.Sales.Sum(s => s.GetEffectiveCardAmount());
        package.TotalExpenses = package.CashTransactions
            .Where(t => t.IsNegative)
            .Sum(t => Math.Abs(t.Amount));

        return package;
    }

    /// <summary>
    /// Paketi .sync (JSON formatında) dosya olarak dışa aktarır.
    /// </summary>
    public async Task ExportPackageToFileAsync(BranchSyncPackage package, string filePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        var json = JsonSerializer.Serialize(package, options);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// .sync dosyasından veri paketini okur.
    /// </summary>
    public async Task<BranchSyncPackage> LoadPackageFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Seçilen senkronizasyon paketi bulunamadı.", filePath);

        var json = await File.ReadAllTextAsync(filePath);
        var package = JsonSerializer.Deserialize<BranchSyncPackage>(json);
        if (package == null)
            throw new InvalidOperationException("Paket içeriği geçerli bir senkronizasyon dosyası değil.");

        return package;
    }

    /// <summary>
    /// İncelenen mağaza paketindeki satış ve hareketleri yerel (veya hedef) veritabanına aktarır.
    /// Çift kayıtları (mükerrerliği) önler, var olan kayıtları günceller.
    /// </summary>
    public async Task<SyncOperationResult> ApplyPackageToDatabaseAsync(BranchSyncPackage package, string? targetConnStr = null)
    {
        var result = new SyncOperationResult();
        string connStr = targetConnStr ?? _dbService.ConnectionString;

        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // 1. Satışları Aktar
            foreach (var sale in package.Sales)
            {
                try
                {
                    // Var mı kontrolü: Aynı tarih, aynı tutar ve ödeme türü
                    var checkSql = @"SELECT Id FROM Sales 
                                     WHERE ABS(DATEDIFF(second, SaleDate, @SaleDate)) <= 5 
                                       AND GrandTotal = @GrandTotal 
                                       AND PaymentType = @PaymentType";
                    long existingId = 0;
                    await using (var checkCmd = new SqlCommand(checkSql, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@SaleDate", sale.SaleDate);
                        checkCmd.Parameters.AddWithValue("@GrandTotal", sale.GrandTotal);
                        checkCmd.Parameters.AddWithValue("@PaymentType", sale.PaymentType);
                        var obj = await checkCmd.ExecuteScalarAsync();
                        if (obj != null && obj != DBNull.Value)
                            existingId = Convert.ToInt64(obj);
                    }

                    if (existingId > 0)
                    {
                        // Var olan satışı güncelle
                        var updateSql = @"UPDATE Sales 
                                          SET SubTotal = @SubTotal, 
                                              VatTotal = @VatTotal, 
                                              GrandTotal = @GrandTotal, 
                                              CostTotal = @CostTotal, 
                                              CashAmount = @CashAmount, 
                                              CardAmount = @CardAmount 
                                          WHERE Id = @Id";
                        await using var updCmd = new SqlCommand(updateSql, conn);
                        updCmd.Parameters.AddWithValue("@Id", existingId);
                        updCmd.Parameters.AddWithValue("@SubTotal", sale.SubTotal);
                        updCmd.Parameters.AddWithValue("@VatTotal", sale.VatTotal);
                        updCmd.Parameters.AddWithValue("@GrandTotal", sale.GrandTotal);
                        updCmd.Parameters.AddWithValue("@CostTotal", sale.CostTotal);
                        updCmd.Parameters.AddWithValue("@CashAmount", sale.CashAmount);
                        updCmd.Parameters.AddWithValue("@CardAmount", sale.CardAmount);
                        await updCmd.ExecuteNonQueryAsync();

                        result.SalesUpdated++;
                    }
                    else
                    {
                        // Yeni satış olarak ekle
                        var insertSql = @"INSERT INTO Sales (WarehouseId, SaleDate, SubTotal, VatTotal, GrandTotal, CostTotal, PaymentType, CashAmount, CardAmount)
                                          VALUES (1, @SaleDate, @SubTotal, @VatTotal, @GrandTotal, @CostTotal, @PaymentType, @CashAmount, @CardAmount);
                                          SELECT SCOPE_IDENTITY();";
                        await using var insCmd = new SqlCommand(insertSql, conn);
                        insCmd.Parameters.AddWithValue("@SaleDate", sale.SaleDate);
                        insCmd.Parameters.AddWithValue("@SubTotal", sale.SubTotal);
                        insCmd.Parameters.AddWithValue("@VatTotal", sale.VatTotal);
                        insCmd.Parameters.AddWithValue("@GrandTotal", sale.GrandTotal);
                        insCmd.Parameters.AddWithValue("@CostTotal", sale.CostTotal);
                        insCmd.Parameters.AddWithValue("@PaymentType", sale.PaymentType);
                        insCmd.Parameters.AddWithValue("@CashAmount", sale.CashAmount);
                        insCmd.Parameters.AddWithValue("@CardAmount", sale.CardAmount);
                        var newSaleIdObj = await insCmd.ExecuteScalarAsync();
                        long newSaleId = Convert.ToInt64(newSaleIdObj);

                        // Kalemleri ekle
                        foreach (var item in sale.Items)
                        {
                            var itemSql = @"INSERT INTO SaleItems (SaleId, ProductId, Quantity, UnitPrice, VatRate, UnitCost)
                                            VALUES (@SaleId, @ProductId, @Quantity, @UnitPrice, @VatRate, @UnitCost)";
                            await using var itemCmd = new SqlCommand(itemSql, conn);
                            itemCmd.Parameters.AddWithValue("@SaleId", newSaleId);
                            itemCmd.Parameters.AddWithValue("@ProductId", item.ProductId > 0 ? item.ProductId : 1);
                            itemCmd.Parameters.AddWithValue("@Quantity", item.Quantity);
                            itemCmd.Parameters.AddWithValue("@UnitPrice", item.UnitPrice);
                            itemCmd.Parameters.AddWithValue("@VatRate", item.VatRate);
                            itemCmd.Parameters.AddWithValue("@UnitCost", item.UnitCost);
                            await itemCmd.ExecuteNonQueryAsync();
                        }

                        // Kredi kartı POS kaydı
                        if (sale.CardAmount > 0)
                        {
                            var posSql = @"INSERT INTO PosTransactions (SaleId, SaleDate, GrossAmount, NetAmount, ExpectedDate, Status)
                                           VALUES (@SaleId, @SaleDate, @Gross, @Net, @Expected, 'Bekliyor')";
                            await using var posCmd = new SqlCommand(posSql, conn);
                            posCmd.Parameters.AddWithValue("@SaleId", newSaleId);
                            posCmd.Parameters.AddWithValue("@SaleDate", sale.SaleDate);
                            posCmd.Parameters.AddWithValue("@Gross", sale.CardAmount);
                            posCmd.Parameters.AddWithValue("@Net", sale.CardAmount * 0.96m);
                            posCmd.Parameters.AddWithValue("@Expected", sale.SaleDate.AddDays(2));
                            await posCmd.ExecuteNonQueryAsync();
                        }

                        result.SalesImported++;
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Satış aktarılamadı (Fiş #{sale.Id}): {ex.Message}");
                }
            }

            // 2. Kasa Hareketlerini Aktar
            foreach (var tx in package.CashTransactions)
            {
                try
                {
                    var txCheckSql = @"SELECT Id FROM CashTransactions 
                                       WHERE ABS(DATEDIFF(second, CreatedAt, @CreatedAt)) <= 5 
                                         AND Amount = @Amount 
                                         AND Type = @Type";
                    bool exists = false;
                    await using (var checkCmd = new SqlCommand(txCheckSql, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@CreatedAt", tx.CreatedAt);
                        checkCmd.Parameters.AddWithValue("@Amount", tx.Amount);
                        checkCmd.Parameters.AddWithValue("@Type", tx.Type);
                        var obj = await checkCmd.ExecuteScalarAsync();
                        exists = obj != null && obj != DBNull.Value;
                    }

                    if (!exists)
                    {
                        var insTxSql = @"INSERT INTO CashTransactions (CashAccountId, Type, Amount, Description, CreatedAt)
                                         VALUES (1, @Type, @Amount, @Description, @CreatedAt)";
                        await using var insCmd = new SqlCommand(insTxSql, conn);
                        insCmd.Parameters.AddWithValue("@Type", tx.Type);
                        insCmd.Parameters.AddWithValue("@Amount", tx.Amount);
                        insCmd.Parameters.AddWithValue("@Description", tx.Description ?? "");
                        insCmd.Parameters.AddWithValue("@CreatedAt", tx.CreatedAt);
                        await insCmd.ExecuteNonQueryAsync();

                        result.TransactionsImported++;
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Kasa hareketi aktarılamadı: {ex.Message}");
                }
            }

            // 3. Kapanışları Aktar
            foreach (var cl in package.Closures)
            {
                try
                {
                    var clCheckSql = @"SELECT Id FROM Closures WHERE ClosureDate = @Date";
                    bool exists = false;
                    await using (var checkCmd = new SqlCommand(clCheckSql, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@Date", cl.ClosureDate.Date);
                        var obj = await checkCmd.ExecuteScalarAsync();
                        exists = obj != null && obj != DBNull.Value;
                    }

                    if (!exists)
                    {
                        var insClSql = @"INSERT INTO Closures (ClosureDate, ClosedAt, SalesCount, TotalRevenue, TotalCash, TotalCreditCard, TotalCost, TotalExpenses, TotalProfit, Notes)
                                         VALUES (@ClosureDate, @ClosedAt, @SalesCount, @TotalRevenue, @TotalCash, @TotalCreditCard, @TotalCost, @TotalExpenses, @TotalProfit, @Notes)";
                        await using var insCmd = new SqlCommand(insClSql, conn);
                        insCmd.Parameters.AddWithValue("@ClosureDate", cl.ClosureDate.Date);
                        insCmd.Parameters.AddWithValue("@ClosedAt", cl.ClosedAt);
                        insCmd.Parameters.AddWithValue("@SalesCount", cl.SalesCount);
                        insCmd.Parameters.AddWithValue("@TotalRevenue", cl.TotalRevenue);
                        insCmd.Parameters.AddWithValue("@TotalCash", cl.TotalCash);
                        insCmd.Parameters.AddWithValue("@TotalCreditCard", cl.TotalCreditCard);
                        insCmd.Parameters.AddWithValue("@TotalCost", cl.TotalCost);
                        insCmd.Parameters.AddWithValue("@TotalExpenses", cl.TotalExpenses);
                        insCmd.Parameters.AddWithValue("@TotalProfit", cl.TotalProfit);
                        insCmd.Parameters.AddWithValue("@Notes", cl.Notes ?? "Şubeden Senkronize Edildi");
                        await insCmd.ExecuteNonQueryAsync();

                        result.ClosuresImported++;
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Kapanış aktarılamadı ({cl.ClosureDate:dd.MM.yyyy}): {ex.Message}");
                }
            }

            result.Success = true;
            result.Message = $"Senkronizasyon tamamlandı: {result.SalesImported} yeni satış, {result.SalesUpdated} güncellenen satış, {result.TransactionsImported} kasa hareketi aktarıldı.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Veritabanına aktarım sırasında hata: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Merkezde yapılan düzeltmeleri (güncelleme veya silme) hedef veritabanına uygular.
    /// </summary>
    public async Task<int> ApplyCorrectionsAsync(List<SyncCorrectionItem> corrections, string? targetConnStr = null)
    {
        if (corrections.Count == 0) return 0;

        string connStr = targetConnStr ?? _dbService.ConnectionString;
        int count = 0;

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        foreach (var c in corrections)
        {
            try
            {
                if (c.TargetType == "Sale")
                {
                    if (c.Action == "Delete")
                    {
                        var delItems = "DELETE FROM SaleItems WHERE SaleId = @Id";
                        await using var cmd1 = new SqlCommand(delItems, conn);
                        cmd1.Parameters.AddWithValue("@Id", c.OriginalId);
                        await cmd1.ExecuteNonQueryAsync();

                        var delPos = "DELETE FROM PosTransactions WHERE SaleId = @Id";
                        await using var cmd2 = new SqlCommand(delPos, conn);
                        cmd2.Parameters.AddWithValue("@Id", c.OriginalId);
                        await cmd2.ExecuteNonQueryAsync();

                        var delSale = "DELETE FROM Sales WHERE Id = @Id";
                        await using var cmd3 = new SqlCommand(delSale, conn);
                        cmd3.Parameters.AddWithValue("@Id", c.OriginalId);
                        await cmd3.ExecuteNonQueryAsync();
                        count++;
                    }
                    else if (c.Action == "Update" && c.UpdatedSale != null)
                    {
                        var sale = c.UpdatedSale;
                        var updSale = @"UPDATE Sales 
                                        SET GrandTotal = @GrandTotal, 
                                            SubTotal = @SubTotal, 
                                            VatTotal = @VatTotal, 
                                            PaymentType = @PaymentType, 
                                            CashAmount = @CashAmount, 
                                            CardAmount = @CardAmount 
                                        WHERE Id = @Id";
                        await using var cmd = new SqlCommand(updSale, conn);
                        cmd.Parameters.AddWithValue("@Id", c.OriginalId);
                        cmd.Parameters.AddWithValue("@GrandTotal", sale.GrandTotal);
                        cmd.Parameters.AddWithValue("@SubTotal", sale.SubTotal);
                        cmd.Parameters.AddWithValue("@VatTotal", sale.VatTotal);
                        cmd.Parameters.AddWithValue("@PaymentType", sale.PaymentType);
                        cmd.Parameters.AddWithValue("@CashAmount", sale.CashAmount);
                        cmd.Parameters.AddWithValue("@CardAmount", sale.CardAmount);
                        await cmd.ExecuteNonQueryAsync();
                        count++;
                    }
                }
                else if (c.TargetType == "CashTransaction")
                {
                    if (c.Action == "Delete")
                    {
                        var delTx = "DELETE FROM CashTransactions WHERE Id = @Id";
                        await using var cmd = new SqlCommand(delTx, conn);
                        cmd.Parameters.AddWithValue("@Id", c.OriginalId);
                        await cmd.ExecuteNonQueryAsync();
                        count++;
                    }
                    else if (c.Action == "Update" && c.UpdatedTransaction != null)
                    {
                        var tx = c.UpdatedTransaction;
                        var updTx = @"UPDATE CashTransactions 
                                      SET Amount = @Amount, 
                                          Description = @Description, 
                                          Type = @Type 
                                      WHERE Id = @Id";
                        await using var cmd = new SqlCommand(updTx, conn);
                        cmd.Parameters.AddWithValue("@Id", c.OriginalId);
                        cmd.Parameters.AddWithValue("@Amount", tx.Amount);
                        cmd.Parameters.AddWithValue("@Description", tx.Description ?? "");
                        cmd.Parameters.AddWithValue("@Type", tx.Type);
                        await cmd.ExecuteNonQueryAsync();
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BranchSyncService] Düzeltme hatası (#{c.OriginalId}): {ex.Message}");
            }
        }

        return count;
    }

    /// <summary>
    /// Merkezde yapılan düzeltmeleri mağazada uygulanmak üzere .sync dosyası olarak paketler.
    /// </summary>
    public async Task ExportCorrectionPackageAsync(List<SyncCorrectionItem> corrections, string filePath)
    {
        var package = new BranchSyncPackage
        {
            IsCorrectionPackage = true,
            Corrections = corrections,
            Notes = $"{corrections.Count} adet düzeltme kaydı içeriyor.",
            CreatedAt = DateTime.Now
        };

        await ExportPackageToFileAsync(package, filePath);
    }

    /// <summary>
    /// Gün sonu yapıldığında değişen verileri (satışlar, ciro, kasa hareketleri, Z raporu)
    /// otomatik olarak paketler ve Merkeze / Bulut klasörüne / Uzak veritabanına iletir.
    /// </summary>
    public async Task<string> AutoDispatchClosureDataAsync(DateTime closureDate, DailyClosure closure)
    {
        var logs = new List<string>();
        try
        {
            var settings = ThemeService.Current ?? new AppSettings();
            var package = await CreateSyncPackageAsync(closureDate, closureDate, settings.StoreName);
            package.Closures = new List<DailyClosure> { closure };
            package.Notes = $"Otomatik Gün Sonu Gönderimi ({closure.ClosedAt:dd.MM.yyyy HH:mm})";

            // 1. Dosya Olarak Arşivleme (Uygulama klasörüne ve Masaüstüne / Seçili Hedef Klasöre)
            string fileName = $"PureAccessory_GunSonu_{closureDate:yyyyMMdd_HHmm}.sync";
            
            // A. Data/AutoSyncExports altına kaydet
            string internalExportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "AutoSyncExports");
            Directory.CreateDirectory(internalExportDir);
            string internalFilePath = Path.Combine(internalExportDir, fileName);
            await ExportPackageToFileAsync(package, internalFilePath);
            logs.Add($"Paket arşivlendi ({fileName})");

            // B. Masaüstü veya Belirtilen Hedef Klasörüne Otomatik Kopyala
            string targetDir = !string.IsNullOrWhiteSpace(settings.AutoSyncTargetDirectory) && Directory.Exists(settings.AutoSyncTargetDirectory)
                ? settings.AutoSyncTargetDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PureAccessory_GunSonu_Paketleri");

            try
            {
                Directory.CreateDirectory(targetDir);
                string targetPath = Path.Combine(targetDir, fileName);
                await ExportPackageToFileAsync(package, targetPath);
                logs.Add($"Masaüstü/Hedef klasöre kaydedildi: {targetPath}");
            }
            catch { }

            // 2. Doğrudan Merkezi / Uzak Veritabanına Otomatik Aktarım (Ağ / IP tanımlıysa)
            if (!string.IsNullOrWhiteSpace(settings.CustomDbConnectionString))
            {
                try
                {
                    var remoteDb = new DatabaseService(settings.CustomDbConnectionString);
                    if (await remoteDb.TestConnectionAsync())
                    {
                        var res = await ApplyPackageToDatabaseAsync(package, settings.CustomDbConnectionString);
                        if (res.Success)
                        {
                            logs.Add($"Merkezi SQL veritabanına doğrudan aktarıldı ({res.SalesImported} yeni satış, {res.TransactionsImported} kasa hareketi).");
                        }
                    }
                    else
                    {
                        logs.Add("Merkezi SQL bağlantısına ulaşılamadı, paket yerel dosyada hazırlandı.");
                    }
                }
                catch (Exception ex)
                {
                    logs.Add($"Merkezi DB aktarım hatası: {ex.Message}");
                }
            }

            // 3. Webhook / HTTP URL (Tanımlıysa)
            if (!string.IsNullOrWhiteSpace(settings.AutoSyncWebhookUrl))
            {
                try
                {
                    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    var json = JsonSerializer.Serialize(package);
                    var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    var resp = await http.PostAsync(settings.AutoSyncWebhookUrl, content);
                    if (resp.IsSuccessStatusCode)
                    {
                        logs.Add("Merkez Webhook servisine iletildi.");
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            logs.Add($"Otomatik gönderim hatası: {ex.Message}");
        }

        return string.Join(" | ", logs);
    }
}
