namespace ERP.Desktop.Models;

public class InterestRate
{
    public int Id { get; set; }
    public int? BankAccountId { get; set; } // Null ise genel kural, varsa hesaba/karta özel oran
    public string AccountType { get; set; } = "Kredi Kartı"; // "Kredi Kartı", "KMH / Ek Hesap", "Kredi"
    public string InterestType { get; set; } = "Akdi Faiz"; // "Akdi Faiz", "Gecikme Faizi"
    public decimal Rate { get; set; } = 4.25m; // Aylık Faiz Oranı (%)
    public DateTime EffectiveDate { get; set; } = DateTime.Today; // Geçerlilik Başlangıcı
    public DateTime? EndDate { get; set; } // Geçerlilik Bitişi (null ise halen yürürlükte)
    public decimal BsmvRate { get; set; } = 5.0m; // %5 BSMV
    public decimal KkdfRate { get; set; } = 15.0m; // %15 KKDF
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string FormattedRate => $"%{Rate:N2}";
    public string FormattedTotalTaxRate => $"%{BsmvRate + KkdfRate:N1} (KKDF %{KkdfRate:N0} + BSMV %{BsmvRate:N0})";
    public string DisplayText => $"{AccountType} - {InterestType}: %{Rate:N2} (Yürürlük: {EffectiveDate:dd.MM.yyyy})";
}
