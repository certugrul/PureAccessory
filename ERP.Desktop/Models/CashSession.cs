namespace ERP.Desktop.Models;

public class CashSession
{
    public long Id { get; set; }
    public DateTime SessionDate { get; set; } = DateTime.Today;
    public DateTime OpenedAt { get; set; } = DateTime.Now;
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public string Status { get; set; } = "Açık"; // "Açık", "Kapalı"
    public string? Notes { get; set; }

    public string FormattedOpeningCash => $"₺{OpeningCash:N2}";
    public string FormattedOpenedAt => OpenedAt.ToString("HH:mm:ss");
    public string FormattedDate => SessionDate.ToString("dd.MM.yyyy");
}

public class CashTransaction
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Type { get; set; } = "Nakit Satış"; // "Kasa Açılışı", "Nakit Satış", "Nakit Giriş", "Nakit Çıkış"
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public string? SourceDocumentType { get; set; } // "SALE", "INVOICE", "CARI", "TRANSFER", "EXPENSE", "SESSION", "ADJUSTMENT"
    public long? SourceDocumentId { get; set; }

    public bool IsNegative => Type.Contains("Çıkış") || Type.Contains("Gider") || Type == "Bankaya Transfer" || Amount < 0;
    public string FormattedAmount => IsNegative ? $"-₺{Math.Abs(Amount):N2}" : $"+₺{Amount:N2}";
    public string FormattedTime => CreatedAt.ToString("HH:mm:ss");
    public string FormattedDate => CreatedAt.ToString("dd.MM.yyyy HH:mm");
}

public class CashSummary
{
    public bool IsOpen { get; set; }
    public DateTime? OpenedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal CashSales { get; set; }
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal TotalVaultBalance { get; set; }

    public decimal CurrentBalance => TotalVaultBalance;

    public string FormattedOpeningCash => $"₺{OpeningCash:N2}";
    public string FormattedCashSales => $"₺{CashSales:N2}";
    public string FormattedCashIn => $"₺{CashIn:N2}";
    public string FormattedCashOut => $"₺{CashOut:N2}";
    public string FormattedCurrentBalance => $"₺{CurrentBalance:N2}";
}
