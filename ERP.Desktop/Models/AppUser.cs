namespace ERP.Desktop.Models;

public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "Kasiyer"; // "Admin", "Kasiyer", "Müdür", "Depo", "Özel"
    public string PinCode { get; set; } = "";     // Giriş şifresi / PIN
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }

    // --- MODÜL ERİŞİM İZİNLERİ ---
    public bool CanAccessSales { get; set; } = true;       // Satış Modülü
    public bool CanAccessProducts { get; set; } = true;    // Ürünler / Stok
    public bool CanAccessWarehouse { get; set; } = false;  // Depo Yönetimi
    public bool CanAccessCari { get; set; } = true;        // Cari Hesaplar
    public bool CanAccessCash { get; set; } = true;        // Kasa Modülü
    public bool CanAccessBank { get; set; } = false;       // Banka Hesapları
    public bool CanAccessDebts { get; set; } = false;      // Borç & Finans Yönetimi
    public bool CanAccessInvoices { get; set; } = false;   // Faturalar
    public bool CanAccessReports { get; set; } = true;     // Raporlar & Analiz
    public bool CanAccessDashboard { get; set; } = false;  // Yönetim Paneli
    public bool CanAccessUsers { get; set; } = false;      // Kullanıcı Yönetimi (Sadece Admin)

    // --- KRİTİK VE FİNANSAL İŞLEM İZİNLERİ ---
    public bool CanTakeClosure { get; set; } = true;       // Gün Sonu Kapanışı (Z Raporu) Yapabilme
    public bool CanCancelReceipt { get; set; } = true;     // Fiş / Satış İptal Edebilme
    public bool CanDeleteRecords { get; set; } = false;    // Hareket / Kayıt / Z Raporu Silme
    public bool CanManagePrices { get; set; } = false;     // Ürün Satış Fiyatını Değiştirme
    public bool CanApplyDiscount { get; set; } = true;     // Fiş / Satır İskonto & İndirim Uygulayabilme
    public bool CanManageDebts { get; set; } = false;      // Borç / Kredi Kartı / KMH Ekleme, Ödeme ve İşlemler
    public bool CanAddCashExpense { get; set; } = true;    // Kasadan Masraf / Nakit Çıkış Yapabilme
    public bool CanDepositToBank { get; set; } = true;     // Kasadan Bankaya Nakit Yatırma

    public bool IsAdmin => Role == "Admin" || CanAccessUsers;
    public bool IsKasiyer => Role == "Kasiyer";

    public string RoleDisplayName => Role switch
    {
        "Admin" => "👑 Yönetici (Admin)",
        "Kasiyer" => "👤 Kasiyer",
        "Müdür" => "💼 Mağaza Müdürü",
        "Depo" => "📦 Depo Sorumlusu",
        _ => $"🛡️ {Role}"
    };

    public string StatusDisplayName => IsActive ? "🟢 Aktif" : "🔴 Pasif";
}
