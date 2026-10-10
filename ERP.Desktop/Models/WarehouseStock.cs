namespace ERP.Desktop.Models;

public class WarehouseStock
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Barcode { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }

    public string FormattedQuantity => $"{Quantity:N0} Adet";
    public string FormattedPurchasePrice => $"₺{PurchasePrice:N2}";
    public string FormattedSalePrice => $"₺{SalePrice:N2}";
    public decimal TotalValue => Math.Round(Quantity * (PurchasePrice > 0 ? PurchasePrice : SalePrice), 2);
    public string FormattedTotalValue => $"₺{TotalValue:N2}";
    public string StockStatus => Quantity <= 0 ? "🔴 Tükendi" : (Quantity < 10 ? "⚠️ Kritik" : "🟢 Yeterli");
}
