namespace ERP.Desktop.Models;

public class Invoice
{
    public long Id { get; set; }
    public string InvoiceNo { get; set; } = "";
    public string Type { get; set; } = "Alış Faturası"; // "Alış Faturası", "Satış Faturası"
    public DateTime InvoiceDate { get; set; } = DateTime.Today;
    public string SupplierOrCustomer { get; set; } = "";
    public string? TaxNo { get; set; }
    public int WarehouseId { get; set; } = 1;
    public string WarehouseName { get; set; } = "Ana Depo";
    public decimal SubTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public int? CariAccountId { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string PaymentType { get; set; } = "Nakit"; // "Nakit", "Banka Hesabı", "Açık Hesap (Cari)"
    public string PaymentStatus { get; set; } = "Ödendi"; // "Ödendi", "Ödenmedi", "Kısmi Ödendi"
    public int? BankAccountId { get; set; }
    public string? BankAccountName { get; set; }
    public string? CurrentAccountName { get; set; }
    public string? Notes { get; set; }
    public List<InvoiceItem> Items { get; set; } = new();

    public string FormattedDate => InvoiceDate.ToString("dd.MM.yyyy");
    public string FormattedGrandTotal => $"₺{GrandTotal:N2}";
    public string FormattedSubTotal => $"₺{SubTotal:N2}";
    public string FormattedVatTotal => $"₺{VatTotal:N2}";
    public string FormattedPaidAmount => $"₺{PaidAmount:N2}";
    public string FormattedRemainingAmount => $"₺{RemainingAmount:N2}";
    public decimal TotalQuantity => Items.Sum(x => x.Quantity);
    public string SummaryText => $"{Items.Count} Çeşit ({TotalQuantity:N0} Adet)";

    public string PaymentSummaryText
    {
        get
        {
            if (PaymentStatus == "Ödendi")
            {
                if (PaymentType == "Banka Hesabı" && !string.IsNullOrWhiteSpace(BankAccountName))
                    return $"🟢 Ödendi ({BankAccountName})";
                return $"🟢 Ödendi ({PaymentType})";
            }
            if (PaymentStatus == "Kısmi Ödendi")
            {
                return $"🟡 Kısmi Ödendi (Kalan: ₺{RemainingAmount:N2})";
            }
            return $"🔴 Açık Hesap / Vadeli ({SupplierOrCustomer})";
        }
    }
}

public class InvoiceItem
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Barcode { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; } = 20;

    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2);
    public decimal VatAmount => Math.Round(LineTotal * (VatRate / 100m), 2);
    public decimal GrandTotal => LineTotal + VatAmount;

    public string FormattedUnitPrice => $"₺{UnitPrice:N2}";
    public string FormattedLineTotal => $"₺{LineTotal:N2}";
    public string FormattedGrandTotal => $"₺{GrandTotal:N2}";
}
