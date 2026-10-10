namespace ERP.Desktop.Models;

public class DebtSummaryDto
{
    public decimal TotalPrincipal { get; set; }
    public decimal TotalAccruedInterest { get; set; }
    public decimal TotalAccruedTax { get; set; }
    public decimal TotalDebt => TotalPrincipal + TotalAccruedInterest + TotalAccruedTax;
    public decimal TotalCreditCardDebt { get; set; }
    public decimal TotalKmhDebt { get; set; }
    public decimal TotalLoanDebt { get; set; }
    public decimal TotalMinPaymentDue { get; set; }

    public string FormattedTotalPrincipal => $"₺{TotalPrincipal:N2}";
    public string FormattedTotalAccruedInterest => $"₺{TotalAccruedInterest:N2}";
    public string FormattedTotalAccruedTax => $"₺{TotalAccruedTax:N2}";
    public string FormattedTotalDebt => $"₺{TotalDebt:N2}";
    public string FormattedTotalCreditCardDebt => $"₺{TotalCreditCardDebt:N2}";
    public string FormattedTotalKmhDebt => $"₺{TotalKmhDebt:N2}";
    public string FormattedTotalLoanDebt => $"₺{TotalLoanDebt:N2}";
    public string FormattedTotalMinPaymentDue => $"₺{TotalMinPaymentDue:N2}";
}
