namespace ERP.Desktop.Models;

public class Payment
{
    public long Id { get; set; }
    public int DebtId { get; set; }
    public int? BankAccountId { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public decimal TotalPaid { get; set; }
    public string PaymentType { get; set; } = "Kısmi"; // "Tam", "Asgari", "Kısmi"
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = "Yönetici";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string FormattedPaid => $"₺{TotalPaid:N2}";
    public string FormattedDate => PaymentDate.ToString("dd.MM.yyyy HH:mm");
}

public class PaymentAllocation
{
    public long Id { get; set; }
    public long PaymentId { get; set; }
    public int DebtId { get; set; }
    public decimal AllocatedInterest { get; set; }
    public decimal AllocatedTax { get; set; }
    public decimal AllocatedExpense { get; set; }
    public decimal AllocatedPrincipal { get; set; }
    public decimal RemainingPrincipalAfter { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string FormattedInterest => $"₺{AllocatedInterest:N2}";
    public string FormattedTax => $"₺{AllocatedTax:N2}";
    public string FormattedExpense => $"₺{AllocatedExpense:N2}";
    public string FormattedPrincipal => $"₺{AllocatedPrincipal:N2}";
    public string FormattedRemainingPrincipal => $"₺{RemainingPrincipalAfter:N2}";
}

public class PaymentBreakdown
{
    public decimal TotalPaid { get; set; }
    public decimal ToInterest { get; set; }
    public decimal ToTax { get; set; }
    public decimal ToExpense { get; set; }
    public decimal ToPrincipal { get; set; }
    public decimal RemainingInterest { get; set; }
    public decimal RemainingTax { get; set; }
    public decimal RemainingPrincipal { get; set; }
    public decimal RemainingTotalDebt => RemainingPrincipal + RemainingInterest + RemainingTax;

    public string FormattedTotalPaid => $"₺{TotalPaid:N2}";
    public string FormattedToInterest => $"₺{ToInterest:N2}";
    public string FormattedToTax => $"₺{ToTax:N2}";
    public string FormattedToPrincipal => $"₺{ToPrincipal:N2}";
    public string FormattedRemainingPrincipal => $"₺{RemainingPrincipal:N2}";
    public string FormattedRemainingTotalDebt => $"₺{RemainingTotalDebt:N2}";
}
