namespace ERP.Desktop.Models;

public enum BarcodeType
{
    Ean13,       // Standart Türkiye 869... (13 haneli perakende)
    StoreEan13,  // Mağaza İçi 200... (13 haneli özel perakende)
    Code128,     // Harf ve rakam içeren alfanümerik kod (örn: PRD-001)
    Custom       // Kullanıcı tanımlı özel prefix
}

public enum LabelPaperFormat
{
    A4_24Labels,    // A4 Sayfa - 24 Etiket (3 Sütun x 8 Satır, ~70x37 mm)
    A4_30Labels,    // A4 Sayfa - 30 Etiket (3 Sütun x 10 Satır, ~70x29.7 mm)
    A4_40Labels,    // A4 Sayfa - 40 Etiket (4 Sütun x 10 Satır, ~52.5x29.7 mm)
    Thermal_60x40,  // Termal Rulo Etiket (60x40 mm)
    Thermal_50x30   // Termal Rulo Etiket (50x30 mm)
}

public class LabelPrintItem
{
    public Product Product { get; set; } = new();
    public int PrintCount { get; set; } = 1;
    public bool IsSelected { get; set; } = true;

    public string DisplayCode => Product.Barcode ?? Product.Code;
}

public class BarcodePrintOptions
{
    public string StoreName { get; set; } = "ERP MARKET";
    public bool ShowStoreName { get; set; } = true;
    public bool ShowProductName { get; set; } = true;
    public bool ShowPrice { get; set; } = true;
    public bool ShowBarcodeText { get; set; } = true;
    public bool ShowQrCode { get; set; } = false;
    public LabelPaperFormat PaperFormat { get; set; } = LabelPaperFormat.A4_24Labels;
}
