namespace ERP.Desktop.Models;

public class DebtTransaction
{
    public long Id { get; set; }
    public int DebtId { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.Now;
    public string TransactionType { get; set; } = "Ödeme"; // "Borç Artışı", "Faiz Tahakkuku", "Vergi Tahakkuku", "Ödeme", "Düzeltme"
    public decimal Amount { get; set; }
    public decimal PrincipalChange { get; set; }
    public decimal InterestChange { get; set; }
    public decimal TaxChange { get; set; }
    public decimal PreviousBalance { get; set; }
    public decimal NewBalance { get; set; }
    public string? Description { get; set; }
    public long? ReferenceId { get; set; }
    public string? ReferenceType { get; set; } // "PAYMENT", "INTEREST_ACCRUAL", "INVOICE", "MANUAL"
    public int? UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string FormattedDate => TransactionDate.ToString("dd.MM.yyyy HH:mm");
    public string FormattedAmount => $"₺{Amount:N2}";
    public string FormattedPrincipalChange => (PrincipalChange >= 0 ? "+" : "") + $"₺{PrincipalChange:N2}";
    public string FormattedInterestChange => (InterestChange >= 0 ? "+" : "") + $"₺{InterestChange:N2}";
    public string FormattedTaxChange => (TaxChange >= 0 ? "+" : "") + $"₺{TaxChange:N2}";
    public string FormattedNewBalance => $"₺{NewBalance:N2}";

    public string TransactionTypeIcon => TransactionType switch
    {
        "Ödeme" => "🟢 💳",
        "Faiz Tahakkuku" => "🟡 📈",
        "Vergi Tahakkuku" => "🏛️ 📊",
        "Borç Artışı" => "🔴 ➕",
        _ => "📝"
    };
}
