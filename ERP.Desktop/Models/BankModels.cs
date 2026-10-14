namespace ERP.Desktop.Models;

public class BankAccount
{
    public int Id { get; set; }
    public string BankName { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Iban { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Currency { get; set; } = "TRY";
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal ActualBalance { get; set; } // Gerçek Banka Bakiyesi (Mutabakat için)
    public decimal PosCommissionRate { get; set; } = 3.99m; // %3,99 varsayılan komisyon
    public int PosValourDays { get; set; } = 2; // Valör günü (satıştan kaç gün sonra yatar)
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // --- KREDİ KARTI & KMH / EK HESAP ALANLARI ---
    public string AccountType { get; set; } = "Vadesiz Mevduat"; // "Vadesiz Mevduat", "Kredi Kartı", "KMH / Ek Hesap"
    public decimal CreditLimit { get; set; } // Toplam Limit
    public decimal AvailableLimit { get; set; } // Kullanılabilir Limit
    public decimal CurrentDebt { get; set; } // Güncel Borç / Kullanılan KMH
    public int? CutoffDay { get; set; } // Hesap Kesim Günü (1-31)
    public int? DueDay { get; set; } // Son Ödeme Günü (1-31)
    public string? CardNumberLast4 { get; set; } // Kart Son 4 Hanesi

    public bool IsCreditCard => AccountType == "Kredi Kartı";
    public bool IsKmh => AccountType == "KMH / Ek Hesap";
    public bool IsDeposit => !IsCreditCard && !IsKmh;

    public string FormattedCurrentBalance => $"₺{CurrentBalance:N2}";
    public string FormattedActualBalance => $"₺{ActualBalance:N2}";
    public string FormattedOpeningBalance => $"₺{OpeningBalance:N2}";
    public string FormattedCommissionRate => $"%{PosCommissionRate:N2}";
    public string FormattedCreditLimit => $"₺{CreditLimit:N2}";
    public string FormattedAvailableLimit => $"₺{AvailableLimit:N2}";
    public string FormattedCurrentDebt => $"₺{CurrentDebt:N2}";

    public decimal ReconciliationDiff => ActualBalance - CurrentBalance;
    public string FormattedReconciliationDiff => (ReconciliationDiff > 0 ? "+" : "") + $"₺{ReconciliationDiff:N2}";
    public bool IsReconciled => Math.Abs(ReconciliationDiff) < 0.01m;
    public string ReconciliationStatus => IsReconciled ? "🟢 Mutabık" : (ReconciliationDiff < 0 ? "🔴 Sistem Fazla" : "🟡 Gerçek Fazla");

    public string DisplayText => IsCreditCard 
        ? $"💳 {BankName} - {AccountName} (Borç: {FormattedCurrentDebt} / Limit: {FormattedCreditLimit})" 
        : (IsKmh ? $"🏧 {BankName} - {AccountName} [KMH] (Kullanılan: {FormattedCurrentDebt} / Limit: {FormattedCreditLimit})" : $"{BankName} - {AccountName} ({FormattedCurrentBalance})");
}

public class BankTransaction
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int BankAccountId { get; set; }
    public string BankAccountName { get; set; } = "";
    public string TransactionType { get; set; } = "Para Girişi";
    public string Direction { get; set; } = "Giriş"; // "Giriş", "Çıkış"
    public decimal Inflow { get; set; }
    public decimal Outflow { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Description { get; set; } = "";
    public string PaymentMethod { get; set; } = "Havale"; // "Havale", "EFT", "FAST", "Kredi Kartı / POS", "Nakit", "Çek", "Diğer"
    public long? ReferenceId { get; set; }
    public string? ReferenceType { get; set; } // "POS", "KASA", "FATURA", "MANUEL"
    public string User { get; set; } = "Yönetici";

    public bool IsInflow => Direction == "Giriş" || Inflow > 0;
    public string FormattedDate => CreatedAt.ToString("dd.MM.yyyy HH:mm");
    public string FormattedTime => CreatedAt.ToString("HH:mm:ss");
    public string FormattedInflow => Inflow > 0 ? $"+₺{Inflow:N2}" : "-";
    public string FormattedOutflow => Outflow > 0 ? $"-₺{Outflow:N2}" : "-";
    public string FormattedAmount => IsInflow ? $"+₺{Inflow:N2}" : $"-₺{Outflow:N2}";
    public string FormattedBalance => $"₺{BalanceAfter:N2}";
}

public class PosTransaction
{
    public long Id { get; set; }
    public long? SaleId { get; set; }
    public DateTime SaleDate { get; set; } = DateTime.Now;
    public decimal GrossAmount { get; set; }
    public decimal CommissionRate { get; set; } = 3.99m;
    public decimal CommissionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public DateTime ExpectedDate { get; set; }
    public DateTime? ActualPaymentDate { get; set; }
    public decimal? ActualPaidAmount { get; set; }
    public int? BankAccountId { get; set; }
    public string? BankAccountName { get; set; }
    public string Status { get; set; } = "Bekliyor"; // "Bekliyor", "Ödendi", "Gecikti"
    public string Description { get; set; } = "";

    public string FormattedGross => $"₺{GrossAmount:N2}";
    public string FormattedCommission => $"₺{CommissionAmount:N2}";
    public string FormattedCommissionRate => $"%{CommissionRate:N2}";
    public string FormattedNet => $"₺{NetAmount:N2}";
    public string FormattedActualPaid => ActualPaidAmount.HasValue ? $"₺{ActualPaidAmount.Value:N2}" : "-";
    public string FormattedSaleDate => SaleDate.ToString("dd.MM.yyyy HH:mm");
    public string FormattedExpectedDate => ExpectedDate.ToString("dd.MM.yyyy");
    public string FormattedActualDate => ActualPaymentDate.HasValue ? ActualPaymentDate.Value.ToString("dd.MM.yyyy HH:mm") : "-";

    public bool IsDueToday => ExpectedDate.Date == DateTime.Today && Status != "Ödendi";
    public bool IsSoldToday => SaleDate.Date == DateTime.Today;

    public string StatusBadge
    {
        get
        {
            if (Status == "Ödendi") return "🟢 Ödendi";
            if (ExpectedDate.Date == DateTime.Today) return "⚡ Vadesi Bugün";
            if (ExpectedDate.Date < DateTime.Today) return "🔴 Gecikti";
            return "🟡 Bekliyor";
        }
    }

    public bool IsPending => Status != "Ödendi";
    public bool IsSettled => Status == "Ödendi";
    public System.Windows.Visibility SettleButtonVisibility => IsPending ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility UnsettleButtonVisibility => IsSettled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
}

public class BankSummary
{
    public decimal TotalBankBalance { get; set; }
    public decimal TodayInflow { get; set; }
    public decimal TodayOutflow { get; set; }
    public decimal PendingGrossPos { get; set; }
    public decimal PendingNetPos { get; set; }
    public decimal PendingPosCommission { get; set; }
    public decimal TodayPosInflow { get; set; }
    public decimal TodayExternalInflow { get; set; }
    public decimal OpeningTotalBalance { get; set; }

    // Günlük POS Satış & Vade Takip Metrikleri
    public decimal TodayPosSalesGross { get; set; }
    public decimal TodayPosSalesNet { get; set; }
    public decimal TodayPosSalesCommission { get; set; }
    public int TodayPosSalesCount { get; set; }

    public decimal TodayDuePosGross { get; set; }
    public decimal TodayDuePosNet { get; set; }
    public decimal TodayDuePosCommission { get; set; }
    public int TodayDuePosCount { get; set; }

    public string FormattedTotalBankBalance => $"₺{TotalBankBalance:N2}";
    public string FormattedTodayInflow => $"₺{TodayInflow:N2}";
    public string FormattedTodayOutflow => $"₺{TodayOutflow:N2}";
    public string FormattedPendingGrossPos => $"₺{PendingGrossPos:N2}";
    public string FormattedPendingNetPos => $"₺{PendingNetPos:N2}";
    public string FormattedPendingPosCommission => $"₺{PendingPosCommission:N2}";
    public string FormattedTodayPosInflow => $"₺{TodayPosInflow:N2}";
    public string FormattedTodayExternalInflow => $"₺{TodayExternalInflow:N2}";
    public string FormattedOpeningTotalBalance => $"₺{OpeningTotalBalance:N2}";

    public string FormattedTodayPosSalesGross => $"₺{TodayPosSalesGross:N2}";
    public string FormattedTodayPosSalesNet => $"₺{TodayPosSalesNet:N2}";
    public string FormattedTodayPosSalesCommission => $"₺{TodayPosSalesCommission:N2}";
    public string FormattedTodayDuePosGross => $"₺{TodayDuePosGross:N2}";
    public string FormattedTodayDuePosNet => $"₺{TodayDuePosNet:N2}";
}

