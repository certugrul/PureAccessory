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

    // Tercihler & Kişiselleştirmeler
    public bool AutoPrintReceipt { get; set; } = false;
    public bool SoundEffectsEnabled { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    public string AppFontFamily { get; set; } = "Segoe UI";
    public string DarkModeOption { get; set; } = "Açık"; // Açık, Koyu, Yumuşak Gri
    public string CornerRadiusOption { get; set; } = "Oval"; // Oval (Modern Yuvarlak), Sade (Hafif), Düz (Keskin)
    public string ReceiptStoreHeader { get; set; } = "PURE ACCESSORY";
    public string ReceiptFooterNote { get; set; } = "Bizi tercih ettiğiniz için teşekkür ederiz!";

    // Kasa Başlangıç Tarihi Filtresi (Veri kaybı olmadan önceki nakitleri hesaba katmama)
    public DateTime? CashBalanceStartDate { get; set; } = new DateTime(2026, 10, 8);

    // Uzaktan Güncelleme Ayarları (GitHub Releases)
    public string UpdateCheckUrl { get; set; } = "https://github.com/certugrul/PureAccessory";
    public bool AutoCheckUpdates { get; set; } = true;

    // Özel Veritabanı Bağlantısı (Uzak SQL Server için)
    public string? CustomDbConnectionString { get; set; }

    // Gün Sonu Otomatik Veri Gönderimi Ayarları
    public bool AutoSyncOnClosure { get; set; } = true;
    public string? AutoSyncTargetDirectory { get; set; }
    public string? AutoSyncWebhookUrl { get; set; }

    // Gün Sonu Z Raporu Otomatik E-Posta Ayarları
    public bool AutoEmailZReportOnClosure { get; set; } = false;
    public string? EmailRecipient { get; set; }
    public string? EmailSender { get; set; }
    public string? EmailSenderName { get; set; } = "Pure Accessory ERP";
    public string? EmailSmtpHost { get; set; } = "smtp.gmail.com";
    public int EmailSmtpPort { get; set; } = 587;
    public bool EmailEnableSsl { get; set; } = true;
    public string? EmailSmtpUser { get; set; }
    public string? EmailSmtpPasswordEncrypted { get; set; }
}
