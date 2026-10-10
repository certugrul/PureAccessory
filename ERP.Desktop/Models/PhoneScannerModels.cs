namespace ERP.Desktop.Models;

public enum PhoneConnectionState
{
    Stopped,
    Listening,
    Connected
}

public class ConnectedPhoneDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DeviceName { get; set; } = "Bilinmeyen Telefon";
    public string IpAddress { get; set; } = "";
    public DateTime ConnectedAt { get; set; } = DateTime.Now;
    public DateTime LastActiveAt { get; set; } = DateTime.Now;
    public int ScanCount { get; set; } = 0;

    public string FormattedConnectedTime => ConnectedAt.ToString("HH:mm:ss");
}

public class BarcodeScanResult
{
    public bool Success { get; set; }
    public string Barcode { get; set; } = "";
    public string? ProductName { get; set; }
    public decimal? Price { get; set; }
    public decimal? QuantityInCart { get; set; }
    public string Message { get; set; } = "";
    public string FormattedPrice => Price.HasValue ? $"₺{Price.Value:N2}" : "";
}

public class PhoneMessageDto
{
    public string? Type { get; set; } // "hello", "scan", "ping", "scan_result"
    public string? Barcode { get; set; }
    public string? Device { get; set; }
    public string? Token { get; set; }
    public bool? Success { get; set; }
    public string? ProductName { get; set; }
    public decimal? Price { get; set; }
    public string? Message { get; set; }
}
