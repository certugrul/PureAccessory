namespace ERP.Desktop.Models;

public class Product
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string? QrCode { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }
    public decimal VatRate { get; set; } = 20;
    public decimal Stock { get; set; }

    public string FormattedSalePrice => $"₺{SalePrice:N2}";
    public string FormattedPurchasePrice => $"₺{PurchasePrice:N2}";
    public string FormattedStock => $"{Stock:N0} Adet";
    public string DisplayCode => !string.IsNullOrWhiteSpace(QrCode) ? QrCode : (!string.IsNullOrWhiteSpace(Barcode) ? Barcode : Code);
}
