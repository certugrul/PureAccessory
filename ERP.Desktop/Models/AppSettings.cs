namespace ERP.Desktop.Models;

public class AppSettings
{
    public string StoreName { get; set; } = "Pure Accessory Otomasyon";
    public string StoreSubtitle { get; set; } = "Satış & Finans Yönetim Sistemi";
    
    // Tema Rengi (Hex)
    public string PrimaryColor { get; set; } = "#2563EB";     // Varsayılan Mavi
    public string PrimaryName { get; set; } = "Klasik Mavi";

    // Sol Menü Rengi (Hex)
    public string SidebarColor { get; set; } = "#111827";    // Koyu Gri / Siyah
    public string SidebarName { get; set; } = "Gece Siyahı";

    // Sayfa Arka Plan Rengi
    public string PageBackgroundColor { get; set; } = "#F3F4F6";

    // Tercihler
    public bool AutoPrintReceipt { get; set; } = false;
    public bool SoundEffectsEnabled { get; set; } = true;
    public bool CompactMode { get; set; } = false;

    // Uzaktan Güncelleme Ayarları
    public string UpdateCheckUrl { get; set; } = "";
    public bool AutoCheckUpdates { get; set; } = true;

    // Özel Veritabanı Bağlantısı (Uzak SQL Server için)
    public string? CustomDbConnectionString { get; set; }

    // Gün Sonu Otomatik Veri Gönderimi Ayarları
    public bool AutoSyncOnClosure { get; set; } = true;
    public string? AutoSyncTargetDirectory { get; set; }
    public string? AutoSyncWebhookUrl { get; set; }
}
