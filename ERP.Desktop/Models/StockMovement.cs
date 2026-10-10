namespace ERP.Desktop.Models;

public class StockMovement
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string MovementType { get; set; } = ""; // "Alış Faturası Girişi", "Satış Çıkışı", "Depo Transferi (+)", "Depo Transferi (-)", "Manuel Düzeltme"
    public decimal Quantity { get; set; }
    public string DocumentNo { get; set; } = "";
    public string Description { get; set; } = "";
    public string? SourceDocumentType { get; set; } // "INVOICE", "SALE", "TRANSFER", "MANUAL"
    public long? SourceDocumentId { get; set; }

    public string FormattedDate => CreatedAt.ToString("dd.MM.yyyy HH:mm");
    public string FormattedQuantity => (Quantity > 0 ? $"+{Quantity:N0}" : $"{Quantity:N0}") + " Adet";
    public bool IsPositive => Quantity > 0;
    public string BadgeColor => IsPositive ? "#059669" : "#DC2626";
}
