using System.Text.Json.Serialization;

namespace ERP.Desktop.Models;

/// <summary>
/// Şubeden merkeze veya merkezden şubeye veri aktarım paketi
/// </summary>
public class BranchSyncPackage
{
    public string PackageId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string BranchName { get; set; } = "Pure Accessory Mağaza";
    public string MachineName { get; set; } = Environment.MachineName;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    // Veri Listeleri
    public List<Sale> Sales { get; set; } = new();
    public List<CashTransaction> CashTransactions { get; set; } = new();
    public List<CashSession> CashSessions { get; set; } = new();
    public List<DailyClosure> Closures { get; set; } = new();

    // Özet Finansal Metrikler
    public decimal TotalRevenue { get; set; }
    public decimal TotalCash { get; set; }
    public decimal TotalCard { get; set; }
    public decimal TotalExpenses { get; set; }
    public int SalesCount { get; set; }

    // Düzeltme Paketi Bilgisi (Merkezden Mağazaya Geri Gönderim)
    public bool IsCorrectionPackage { get; set; } = false;
    public List<SyncCorrectionItem> Corrections { get; set; } = new();

    public string Notes { get; set; } = "";

    // Görüntüleme Formatlayıcıları
    [JsonIgnore]
    public string FormattedTotalRevenue => $"₺{TotalRevenue:N2}";
    [JsonIgnore]
    public string FormattedTotalCash => $"₺{TotalCash:N2}";
    [JsonIgnore]
    public string FormattedTotalCard => $"₺{TotalCard:N2}";
    [JsonIgnore]
    public string FormattedTotalExpenses => $"₺{TotalExpenses:N2}";
    [JsonIgnore]
    public string FormattedDateRange => $"{StartDate:dd.MM.yyyy} - {EndDate:dd.MM.yyyy}";
    [JsonIgnore]
    public string FormattedCreatedAt => CreatedAt.ToString("dd.MM.yyyy HH:mm:ss");
}

/// <summary>
/// Merkezde yapılan ve mağazaya geri yansıtılacak düzeltme kalemi
/// </summary>
public class SyncCorrectionItem
{
    public string CorrectionId { get; set; } = Guid.NewGuid().ToString("N");
    public string TargetType { get; set; } = "Sale"; // "Sale", "CashTransaction"
    public long OriginalId { get; set; }
    public string Action { get; set; } = "Update"; // "Update", "Delete"
    
    // Düzenlenen nesneler
    public Sale? UpdatedSale { get; set; }
    public CashTransaction? UpdatedTransaction { get; set; }
    
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string DisplayText => TargetType switch
    {
        "Sale" => $"Fiş #{OriginalId}: {(Action == "Delete" ? "İptal Edildi" : $"₺{UpdatedSale?.GrandTotal:N2} olarak güncellendi")} ({Reason})",
        "CashTransaction" => $"Kasa Hareketi #{OriginalId}: {(Action == "Delete" ? "Silindi" : $"₺{UpdatedTransaction?.Amount:N2} olarak güncellendi")} ({Reason})",
        _ => $"{TargetType} #{OriginalId} {Action}"
    };
}

/// <summary>
/// Senkronizasyon işlem sonucu
/// </summary>
public class SyncOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int SalesImported { get; set; }
    public int SalesUpdated { get; set; }
    public int TransactionsImported { get; set; }
    public int TransactionsUpdated { get; set; }
    public int ClosuresImported { get; set; }
    public int CorrectionsApplied { get; set; }
    public List<string> Warnings { get; set; } = new();
}
