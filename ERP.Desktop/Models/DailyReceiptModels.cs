namespace ERP.Desktop.Models;

public class DailySoldProductSummary
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal TotalQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatTotal { get; set; }
    public decimal TotalRevenue { get; set; }
    public int ReceiptCount { get; set; }
    public decimal RevenueSharePercent { get; set; }

    public string FormattedQuantity => $"{TotalQuantity:N0} Adet";
    public string FormattedUnitPrice => $"₺{UnitPrice:N2}";
    public string FormattedVatTotal => $"₺{VatTotal:N2}";
    public string FormattedTotalRevenue => $"₺{TotalRevenue:N2}";
    public string FormattedShare => $"%{RevenueSharePercent:N1}";
}

public class DailyReceiptRow
{
    public long SaleId { get; set; }
    public DateTime SaleDate { get; set; }
    public string PaymentType { get; set; } = "Nakit";
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal TotalQuantity { get; set; }
    public int ItemCount { get; set; }
    public decimal SubTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public List<SaleItem> Items { get; set; } = new();

    public string FormattedId => $"#{SaleId}";
    public string FormattedTime => SaleDate.ToString("HH:mm:ss");
    public string FormattedDate => SaleDate.ToString("dd.MM.yyyy HH:mm");
    public string FormattedQuantity => $"{TotalQuantity:N0} Adet";
    public string FormattedItemCount => $"{ItemCount} Çeşit";
    public string FormattedTotal => $"₺{GrandTotal:N2}";
    public string PaymentTypeBadge => PaymentType switch
    {
        "Nakit" => "💵 Nakit",
        "Kredi Kartı" or "Kart" => "💳 Kredi Kartı",
        "Parçalı" => $"⚖️ Parçalı (₺{CashAmount:N0} N + ₺{CardAmount:N0} K)",
        "Veresiye" => "📝 Veresiye",
        _ => PaymentType
    };
    public string FormattedPaymentDetails => PaymentType == "Parçalı"
        ? $"Parçalı (₺{CashAmount:N2} Nakit + ₺{CardAmount:N2} Kredi Kartı)"
        : PaymentType;
}

public class DailyReceiptBreakdownData
{
    public DateTime TargetDate { get; set; }
    public int TotalSalesCount { get; set; }
    public decimal TotalQuantitySold { get; set; }
    public int TotalDistinctProducts { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal CashRevenue { get; set; }
    public decimal CreditCardRevenue { get; set; }
    public decimal OnAccountRevenue { get; set; }
    public List<DailySoldProductSummary> SoldProducts { get; set; } = new();
    public List<DailyReceiptRow> Receipts { get; set; } = new();
}
