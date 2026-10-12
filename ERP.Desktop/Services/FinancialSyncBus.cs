using System;

namespace ERP.Desktop.Services;

public enum FinancialChangeType
{
    CashExpense,
    CashInflow,
    CashSales,
    PosSales,
    PosSettlement,
    BankTransaction,
    CashBankTransfer,
    DayClosure,
    CariTransaction
}

/// <summary>
/// Tüm ERP ekranları (Kasa, Banka, Satış, Dashboard, Raporlar) arasında
/// finansal bir hareket olduğunda anında canlı haberleşmeyi sağlayan
/// merkezi, thread-safe olay veriyoludur.
/// </summary>
public static class FinancialSyncBus
{
    public static event Action<FinancialChangeType, object?>? FinancialDataChanged;

    /// <summary>
    /// Bir finansal işlem DB'de başarıyla tamamlandığında tüm açık ekranları anında haberdar eder.
    /// </summary>
    public static void Notify(FinancialChangeType type, object? payload = null)
    {
        try
        {
            FinancialDataChanged?.Invoke(type, payload);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FinancialSyncBus] Bildirim hatası: {ex.Message}");
        }
    }
}
