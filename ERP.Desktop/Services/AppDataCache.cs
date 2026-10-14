using System.Collections.Concurrent;
using ERP.Desktop.Models;

namespace ERP.Desktop.Services;

/// <summary>
/// Uygulamaya girmeden önce SQL Server veritabanından tüm operasyonel verileri
/// önceden yükleyen (pre-load) ve RAM üzerinde hazır tutan merkezi önbellek servisidir.
/// Kullanıcı giriş yaptığında modüller (Satış, Ürünler, Kasa, Cari, Depo, Banka)
/// bekleme ve boş ekran görmeden anında hazır verilerle açılır.
/// Ağır tarihsel raporlar ise kullanıcının talebine uygun olarak on-demand kalır.
/// </summary>
public static class AppDataCache
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    public static bool IsLoaded { get; private set; } = false;

    // Önbellekteki temel operasyonel veriler
    public static List<Product> Products { get; private set; } = new();
    public static List<Warehouse> Warehouses { get; private set; } = new();
    public static List<WarehouseStock> WarehouseStocks { get; private set; } = new();
    public static List<CariAccount> CariAccounts { get; private set; } = new();
    public static List<BankAccount> BankAccounts { get; private set; } = new();
    public static CashSession? TodayCashSession { get; private set; }
    public static CashSummary? TodayCashSummary { get; private set; }
    public static decimal CentralCashBalance { get; private set; } = 0;
    public static BankSummary? BankSummary { get; private set; }

    /// <summary>
    /// Veritabanındaki tüm operasyonel tabloları tek seferde ön yükler.
    /// İlerleme durumunu UI'a (IProgress) aktarır.
    /// </summary>
    public static async Task PreloadAllAsync(IProgress<string>? progress = null)
    {
        await _lock.WaitAsync();
        try
        {
            var dbService = new DatabaseService();
            var coordinator = new FinancialTransactionCoordinator(dbService);
            var productService = new ProductService(dbService);
            var warehouseService = new WarehouseService(dbService, productService);
            var cariService = new CariService(dbService, coordinator);
            var bankService = new BankService(dbService);
            var cashService = new CashService(dbService, new SalesService(dbService), coordinator);

            progress?.Report("Veritabanı bağlantısı doğrulanıyor...");
            bool isConnected = await dbService.TestConnectionAsync();

            progress?.Report("1/6: Ürünler ve stok kartları veritabanından çekiliyor...");
            var productsTask = productService.GetAllProductsAsync();

            progress?.Report("2/6: Depolar ve depo stokları yükleniyor...");
            var warehousesTask = warehouseService.GetAllWarehousesAsync();

            progress?.Report("3/6: Cari hesap kartları ve bakiyeler alınıyor...");
            var cariTask = cariService.GetAllAccountsAsync();

            progress?.Report("4/6: Banka hesapları ve POS durumu alınıyor...");
            var bankTask = bankService.GetAllAccountsAsync();
            var bankSummaryTask = bankService.GetBankSummaryAsync();

            progress?.Report("5/6: Kasa durumu ve merkezi nakit hesaplanıyor...");
            var cashSessionTask = cashService.GetTodaySessionAsync(DateTime.Today);
            var cashSummaryTask = cashService.GetCashSummaryAsync(DateTime.Today);
            var cashBalanceTask = coordinator.GetCentralCashBalanceAsync();

            // Tüm sorguları paralel ve güvenli şekilde tamamla (zaman aşımı korumalı)
            var allTasks = Task.WhenAll(
                productsTask,
                warehousesTask,
                cariTask,
                bankTask,
                bankSummaryTask,
                cashSessionTask,
                cashSummaryTask,
                cashBalanceTask
            );

            if (await Task.WhenAny(allTasks, Task.Delay(4000)) == allTasks)
            {
                Products = await productsTask;
                Warehouses = await warehousesTask;
                CariAccounts = await cariTask;
                BankAccounts = await bankTask;
                BankSummary = await bankSummaryTask;
                TodayCashSession = await cashSessionTask;
                TodayCashSummary = await cashSummaryTask;
                CentralCashBalance = await cashBalanceTask;
            }

            progress?.Report("6/6: Depo ürün dağılımları senkronize ediliyor...");
            try
            {
                WarehouseStocks = await warehouseService.GetWarehouseStocksAsync();
            }
            catch
            {
                WarehouseStocks = new List<WarehouseStock>();
            }

            IsLoaded = true;
            progress?.Report("Tüm veriler hazırlandı. Sisteme giriş yapılıyor...");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppDataCache] Preload error: {ex.Message}");
            // Hata olsa dahi devam edebilmek için IsLoaded = true işaretlenir
            IsLoaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Kasa açılışı veya kapanışı yapıldığında önbellekteki oturumu anında günceller.
    /// </summary>
    public static void UpdateTodayCashSession(CashSession? session)
    {
        TodayCashSession = session;
    }

    public static void UpdateProducts(List<Product> products)
    {
        Products = products;
    }

    public static void UpdateCariAccounts(List<CariAccount> accounts)
    {
        CariAccounts = accounts;
    }

    public static void UpdateCashState(CashSummary? summary, decimal balance, CashSession? session = null)
    {
        if (summary != null) TodayCashSummary = summary;
        CentralCashBalance = balance;
        if (session != null) TodayCashSession = session;
    }

    public static void UpdateBankAccounts(List<BankAccount> accounts, BankSummary? summary = null)
    {
        BankAccounts = accounts;
        if (summary != null) BankSummary = summary;
    }

    /// <summary>
    /// Herhangi bir işlem (Satış, Kasa Girişi, Ürün Ekleme vb.) yapıldığında
    /// önbellekteki veriyi hızlıca tazelemek için kullanılır.
    /// </summary>
    public static void Invalidate()
    {
        IsLoaded = false;
    }
}
