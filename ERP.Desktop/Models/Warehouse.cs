namespace ERP.Desktop.Models;

public class Warehouse
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string? Description { get; set; }
    public bool IsDefault { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DisplayText => $"{Name} ({Code})";
    public string DefaultBadge => IsDefault ? "⭐ Varsayılan" : "";
}
