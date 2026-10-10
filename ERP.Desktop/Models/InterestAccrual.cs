namespace ERP.Desktop.Models;

public class InterestAccrual
{
    public long Id { get; set; }
    public int DebtId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int Days { get; set; }
    public decimal Rate { get; set; } // Kullanılan aylık faiz oranı (%)
    public decimal PrincipalAmount { get; set; } // Faiz işletilen anapara
    public decimal CalculatedInterest { get; set; } // Net tahakkuk eden faiz
    public decimal BsmvAmount { get; set; } // BSMV tutarı (%5)
    public decimal KkdfAmount { get; set; } // KKDF tutarı (%15)
    public decimal TotalTax { get; set; } // KKDF + BSMV
    public decimal TotalAccrual { get; set; } // Faiz + Vergiler
    public bool IsApplied { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string FormattedPeriod => $"{FromDate:dd.MM.yyyy} - {ToDate:dd.MM.yyyy} ({Days} gün)";
    public string FormattedPrincipal => $"₺{PrincipalAmount:N2}";
    public string FormattedInterest => $"₺{CalculatedInterest:N2}";
    public string FormattedTax => $"₺{TotalTax:N2}";
    public string FormattedTotalAccrual => $"₺{TotalAccrual:N2}";
}
