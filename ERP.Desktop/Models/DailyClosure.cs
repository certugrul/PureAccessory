namespace ERP.Desktop.Models;

public class DailyClosure
{
    public long Id { get; set; }
    public DateTime ClosureDate { get; set; } = DateTime.Today;
    public DateTime ClosedAt { get; set; } = DateTime.Now;
    public decimal TotalRevenue { get; set; }
    public decimal TotalCash { get; set; }
    public decimal TotalCreditCard { get; set; }
    public decimal TotalOnAccount { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal TotalExpenses { get; set; }
    public int SalesCount { get; set; }
    public string? Notes { get; set; }

    // Excel Geçmiş Entegrasyon Alanları
    public decimal ZReportCash { get; set; }
    public decimal ZReportCredit { get; set; }
    public decimal CashDiff { get; set; }
    public decimal CreditDiff { get; set; }
    public decimal CashExpense { get; set; }
    public decimal CreditExpense { get; set; }
    public decimal VatAmount { get; set; }
    public decimal PocketCash { get; set; }
    public decimal PocketCredit { get; set; }
    public decimal IbanAmount { get; set; }
    public decimal ExpectedBankPayment { get; set; }

    public string? Source { get; set; }
    public string? SourceSheet { get; set; }
    public int? SourceRow { get; set; }

    public string FormattedDate => ClosureDate.ToString("dd.MM.yyyy");
    public string FormattedTime => ClosedAt.ToString("HH:mm:ss");
    public string FormattedRevenue => $"₺{TotalRevenue:N2}";
    public string FormattedProfit => $"₺{TotalProfit:N2}";
    public string FormattedCash => $"₺{TotalCash:N2}";
    public string FormattedCreditCard => $"₺{TotalCreditCard:N2}";
    public string FormattedZReportCredit => $"₺{ZReportCredit:N2}";
    public string FormattedIbanAmount => $"₺{IbanAmount:N2}";
    public string FormattedCashExpense => $"₺{CashExpense:N2}";
    public string FormattedCreditExpense => $"₺{CreditExpense:N2}";
    public string FormattedVatAmount => $"₺{VatAmount:N2}";
    public string FormattedPocketCash => $"₺{PocketCash:N2}";
    public string FormattedPocketCredit => $"₺{PocketCredit:N2}";
}
