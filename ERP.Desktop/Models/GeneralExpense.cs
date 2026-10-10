namespace ERP.Desktop.Models;

public class GeneralExpense
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Kira"; // "Kira", "Fatura", "Maaş/SGK", "Vergi", "Tedarikçi", "Diğer"
    public decimal MonthlyAmount { get; set; }
    public int DayOfMonth { get; set; } = 15;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public string FormattedAmount => $"₺{MonthlyAmount:N2}";
    public string CategoryIcon => Category switch
    {
        "Kira" => "🏢",
        "Kredi Kartı" => "💳",
        "Fatura" => "⚡",
        "Maaş/SGK" => "👥",
        "Vergi" => "🏛️",
        "Tedarikçi" => "📦",
        _ => "📝"
    };
}
