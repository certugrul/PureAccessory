namespace ERP.Desktop.Models;

public class CariAccount
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Müşteri"; // "Müşteri", "Tedarikçi", "Diğer"
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? TaxNo { get; set; }
    public string? TaxOffice { get; set; }
    public string? Address { get; set; }
    public decimal Balance { get; set; } // Pozitif: Alacaklıyız (Müşteri borcu), Negatif: Borçluyuz (Tedarikçi alacağı)
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string FormattedBalance => $"₺{Math.Abs(Balance):N2}";
    public string BalanceStatus => Balance > 0 ? "🟢 Alacak (Müşteri Borcu)" : (Balance < 0 ? "🔴 Borç (Ödenecek)" : "⚪ Bakiye Sıfır");
    public string DisplayText => $"{Name} ({Type}) - {FormattedBalance}";
}

public class CariTransaction
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CariAccountId { get; set; }
    public string CariAccountName { get; set; } = "";
    public string TransactionType { get; set; } = "Veresiye Satış"; // "Veresiye Satış", "Açık Hesap Fatura", "Tahsilat", "Ödeme", "Manuel Borç", "Manuel Alacak"
    public string DocumentNo { get; set; } = "";
    public decimal Debit { get; set; }  // Borç (+) -> Müşteriye borç yazıldı veya tedarikçiye ödeme yapıldı
    public decimal Credit { get; set; } // Alacak (-) -> Müşteriden tahsil edildi veya tedarikçiden mal alındı
    public decimal BalanceAfter { get; set; }
    public string Description { get; set; } = "";
    public long? InvoiceId { get; set; }
    public long? SaleId { get; set; }
    public string? PaymentMethod { get; set; } // "Nakit", "Banka Hesabı", "Açık Hesap"

    public string FormattedDate => CreatedAt.ToString("dd.MM.yyyy HH:mm");
    public string FormattedDebit => Debit > 0 ? $"₺{Debit:N2}" : "-";
    public string FormattedCredit => Credit > 0 ? $"₺{Credit:N2}" : "-";
    public string FormattedBalance => $"₺{Math.Abs(BalanceAfter):N2}";
}
