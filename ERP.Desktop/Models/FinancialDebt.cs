namespace ERP.Desktop.Models;

public class FinancialDebt
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Creditor { get; set; } = ""; // Alacaklı / Kurum (Banka, Şahıs vb.)
    public string DebtType { get; set; } = "Kredi / Borç"; // "Kredi Kartı", "KMH / Ek Hesap", "Taksitli Kredi", "Şahıs / Senet Borcu", "Diğer"
    public int? BankAccountId { get; set; } // Bağlı banka/kart hesabı
    public string? BankAccountName { get; set; }

    public decimal TotalAmount { get; set; }    // Toplam Borç Tutarı (veya Kredi Açılış Limiti/Anaparası)
    public decimal RemainingAmount { get; set; } // Kalan Borç Tutarı (Anapara + Birikmiş Faiz & Vergi)
    public decimal PrincipalAmount { get; set; } // Kalan Saf Anapara Tutarı
    public decimal AccruedInterest { get; set; } // Birikmiş / Tahakkuk Eden Net Faiz
    public decimal AccruedTax { get; set; }      // Birikmiş Vergi (KKDF + BSMV)
    public decimal MinPaymentAmount { get; set; } // Asgari Ödeme Tutarı

    public decimal InterestRate { get; set; }     // Aylık Akdi Faiz Oranı (%)
    public decimal LateInterestRate { get; set; } // Aylık Gecikme Faizi Oranı (%)
    public DateTime? LastInterestCalculationDate { get; set; } // Son Faiz Hesaplama / Tahakkuk Tarihi
    public DateTime? CutoffDate { get; set; }     // Hesap Kesim Tarihi

    public decimal InstallmentAmount { get; set; } // Aylık Taksit / Ödeme Tutarı
    public int TotalInstallments { get; set; }   // Toplam Taksit Sayısı
    public int RemainingInstallments { get; set; } // Kalan Taksit Sayısı
    public int DayOfMonth { get; set; } = 15;   // Her Ayın Vade Günü (1-31)
    public DateTime StartDate { get; set; } = DateTime.Today;
    public string Status { get; set; } = "Devam Ediyor"; // "Devam Ediyor", "Tamamlandı / Kapandı"
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    // --- HESAPLANAN ÖZELLİKLER ---
    public decimal TotalPayableDebt => PrincipalAmount > 0 
        ? PrincipalAmount + AccruedInterest + AccruedTax 
        : RemainingAmount;

    public bool IsCompleted => (PrincipalAmount <= 0 && RemainingAmount <= 0) || Status == "Tamamlandı / Kapandı";
    
    public string FormattedTotal => $"₺{TotalAmount:N2}";
    public string FormattedRemaining => $"₺{RemainingAmount:N2}";
    public string FormattedPrincipal => $"₺{PrincipalAmount:N2}";
    public string FormattedAccruedInterest => $"₺{AccruedInterest:N2}";
    public string FormattedAccruedTax => $"₺{AccruedTax:N2}";
    public string FormattedMinPayment => $"₺{MinPaymentAmount:N2}";
    public string FormattedTotalPayable => $"₺{TotalPayableDebt:N2}";
    public string FormattedInstallment => $"₺{InstallmentAmount:N2}";
    public string FormattedStartDate => StartDate.ToString("dd.MM.yyyy");

    public string StatusBadge => IsCompleted 
        ? "🟢 Tamamlandı (Bitti)" 
        : $"🟡 Devam Ediyor (Anapara: ₺{PrincipalAmount:N0} | Toplam: ₺{TotalPayableDebt:N0})";
}
