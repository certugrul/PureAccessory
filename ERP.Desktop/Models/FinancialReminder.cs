namespace ERP.Desktop.Models;

public class FinancialReminder
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Kira"; // "Kira", "Kredi Kartı", "Fatura", "Maaş/SGK", "Vergi", "Tedarikçi", "Borç / Kredi", "Diğer"
    public string ReminderType { get; set; } = "Genel Gider"; // "Genel Gider", "Borç / Kredi", "Özel"
    public int? GeneralExpenseId { get; set; }
    public int? DebtId { get; set; }
    public decimal? RemainingDebtAfter { get; set; }
    public decimal? MinPayment { get; set; }
    public decimal? AccruedInterest { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? PrincipalPaid { get; set; }
    public decimal? CurrentTotalDebt { get; set; }
    public DateTime? InterestStartDate { get; set; }

    public decimal Amount { get; set; }
    public int DayOfMonth { get; set; } = 15;
    public DateTime DueDate { get; set; } = DateTime.Today;
    public bool IsRecurringMonthly { get; set; } = true;
    public string Status { get; set; } = "Bekliyor"; // "Bekliyor", "Ödendi"
    public DateTime? PaidDate { get; set; }
    public string? PaidFrom { get; set; } // "Banka: Ziraat Bankası", "Nakit Kasa"
    public int? BankAccountId { get; set; }
    public string? InvoiceNo { get; set; }
    public string? Description { get; set; }
    public bool IsDeleted { get; set; } = false;

    public bool WasAdjustedFromSaturday => DueDate.DayOfWeek == DayOfWeek.Monday && 
        (DueDate.Day - 2 == DayOfMonth || (DueDate.Day <= 2 && DueDate.AddDays(-2).Day == DayOfMonth));

    public bool WasAdjustedFromSunday => DueDate.DayOfWeek == DayOfWeek.Monday && 
        (DueDate.Day - 1 == DayOfMonth || (DueDate.Day <= 1 && DueDate.AddDays(-1).Day == DayOfMonth));

    public bool WasAdjustedFromWeekend => WasAdjustedFromSaturday || WasAdjustedFromSunday;

    public string WeekendAdjustmentBadge => WasAdjustedFromSaturday ? " (Cmt ➔ Pzt)" : (WasAdjustedFromSunday ? " (Pzr ➔ Pzt)" : "");

    public string FormattedAmount => $"₺{Amount:N2}";
    public string FormattedDueDate => DueDate.ToString("dd.MM.yyyy") + WeekendAdjustmentBadge;
    public bool IsPaid => Status == "Ödendi";

    public string CategoryIcon => Category switch
    {
        "Kira" => "🏢",
        "Kredi Kartı" => "💳",
        "Borç / Kredi" => "🏦",
        "Fatura" => "⚡",
        "Maaş/SGK" => "👥",
        "Vergi" => "🏛️",
        "Tedarikçi" => "📦",
        _ => "📝"
    };

    public string TypeBadge => ReminderType switch
    {
        "Borç / Kredi" => "💳 Borç Taksiti",
        "Genel Gider" => "🏢 Genel Gider",
        _ => "📝 Özel Bildirim"
    };

    public string StatusBadge => IsPaid 
        ? $"🟢 Ödendi {(string.IsNullOrWhiteSpace(PaidFrom) ? "" : $"({PaidFrom})")}"
        : "🟡 Bekliyor";
}
