namespace ERP.Desktop.Models;

public class FinancialAuditLog
{
    public long Id { get; set; }
    public string ActionType { get; set; } = "UPDATE"; // "INSERT", "UPDATE", "DELETE", "PAYMENT", "ACCRUAL"
    public string EntityName { get; set; } = ""; // "FinancialDebt", "BankAccount", "Payment", "InterestRate"
    public long? EntityId { get; set; }
    public int? BankAccountId { get; set; }
    public int? DebtId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Description { get; set; } = "";
    public int? UserId { get; set; }
    public string UserName { get; set; } = "Sistem";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string FormattedDate => CreatedAt.ToString("dd.MM.yyyy HH:mm:ss");
}
