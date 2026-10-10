using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ERP.Desktop.Models;

public class Sale
{
    public long Id { get; set; }
    public DateTime SaleDate { get; set; } = DateTime.Now;
    public decimal GrossTotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal ReceiptDiscount { get; set; }
    public decimal DiscountTotal => LineDiscountTotal + ReceiptDiscount;
    public decimal SubTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal CostTotal { get; set; }
    public decimal Profit => GrandTotal - CostTotal;
    public string PaymentType { get; set; } = "Nakit"; // Nakit, Kredi Kartı, Veresiye, Parçalı
    public int? CustomerId { get; set; }
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public List<SaleItem> Items { get; set; } = new();

    public decimal GetEffectiveCashAmount()
    {
        if (PaymentType == "Parçalı") return CashAmount;
        if (PaymentType == "Nakit") return GrandTotal;
        return 0m;
    }

    public decimal GetEffectiveCardAmount()
    {
        if (PaymentType == "Parçalı") return CardAmount;
        if (PaymentType == "Kredi Kartı" || PaymentType == "Kart") return GrandTotal;
        return 0m;
    }

    public decimal GetEffectiveOnAccountAmount()
    {
        if (PaymentType == "Veresiye") return GrandTotal;
        return 0m;
    }

    public string FormattedDate => SaleDate.ToString("dd.MM.yyyy HH:mm");
    public string FormattedTotal => $"₺{GrandTotal:N2}";
    public string FormattedDiscountTotal => $"₺{DiscountTotal:N2}";
    public string FormattedPaymentDetails => PaymentType == "Parçalı"
        ? $"Parçalı (₺{CashAmount:N2} Nakit + ₺{CardAmount:N2} Kart)"
        : PaymentType;
}

public class SaleItem
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; } = 20;
    public decimal UnitCost { get; set; }
    public decimal LineTotal => Math.Max(0, (Quantity * UnitPrice) - DiscountAmount);
}

public class CartItem : INotifyPropertyChanged
{
    private int _quantity = 1;
    private decimal _unitPrice;
    private decimal _discountAmount;
    private decimal _discountPercent;

    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string? QrCode { get; set; }
    public decimal OriginalUnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal VatRate { get; set; } = 20;
    public decimal AvailableStock { get; set; }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (_unitPrice != value && value >= 0)
            {
                _unitPrice = value;
                if (_discountPercent > 0)
                {
                    _discountAmount = Math.Round((_quantity * _unitPrice) * (_discountPercent / 100m), 2);
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(GrossTotal));
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(FormattedUnitPrice));
                OnPropertyChanged(nameof(FormattedTotal));
                OnPropertyChanged(nameof(FormattedDiscount));
                OnPropertyChanged(nameof(HasDiscount));
                OnPropertyChanged(nameof(IsPriceOverridden));
                OnPropertyChanged(nameof(DiscountBadgeVisibility));
                OnPropertyChanged(nameof(NoDiscountButtonVisibility));
                OnPropertyChanged(nameof(OriginalPriceVisibility));
            }
        }
    }

    public decimal DiscountPercent
    {
        get => _discountPercent;
        set
        {
            if (_discountPercent != value && value >= 0 && value <= 100)
            {
                _discountPercent = value;
                _discountAmount = Math.Round((Quantity * UnitPrice) * (_discountPercent / 100m), 2);
                OnPropertyChanged();
                OnPropertyChanged(nameof(DiscountAmount));
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(FormattedTotal));
                OnPropertyChanged(nameof(FormattedDiscount));
                OnPropertyChanged(nameof(HasDiscount));
                OnPropertyChanged(nameof(DiscountBadgeVisibility));
                OnPropertyChanged(nameof(NoDiscountButtonVisibility));
            }
        }
    }

    public decimal DiscountAmount
    {
        get => _discountAmount;
        set
        {
            if (_discountAmount != value && value >= 0)
            {
                _discountAmount = value;
                var gross = Quantity * UnitPrice;
                _discountPercent = gross > 0 ? Math.Round((_discountAmount / gross) * 100m, 1) : 0;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DiscountPercent));
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(FormattedTotal));
                OnPropertyChanged(nameof(FormattedDiscount));
                OnPropertyChanged(nameof(HasDiscount));
                OnPropertyChanged(nameof(DiscountBadgeVisibility));
                OnPropertyChanged(nameof(NoDiscountButtonVisibility));
            }
        }
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity != value && value >= 1)
            {
                _quantity = value;
                if (_discountPercent > 0)
                {
                    _discountAmount = Math.Round((_quantity * UnitPrice) * (_discountPercent / 100m), 2);
                    OnPropertyChanged(nameof(DiscountAmount));
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(GrossTotal));
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(FormattedTotal));
                OnPropertyChanged(nameof(FormattedDiscount));
            }
        }
    }

    public decimal GrossTotal => Quantity * UnitPrice;
    public decimal Total => Math.Max(0, GrossTotal - DiscountAmount);
    public bool HasDiscount => DiscountAmount > 0;
    public bool IsPriceOverridden => OriginalUnitPrice > 0 && Math.Round(UnitPrice, 2) != Math.Round(OriginalUnitPrice, 2);

    public string FormattedUnitPrice => $"₺{UnitPrice:N2}";
    public string FormattedOriginalUnitPrice => $"₺{OriginalUnitPrice:N2}";
    public string FormattedTotal => $"₺{Total:N2}";
    public string FormattedDiscount => HasDiscount ? $"-₺{DiscountAmount:N2} (%{DiscountPercent:N0})" : "-";

    public System.Windows.Visibility DiscountBadgeVisibility => HasDiscount ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility NoDiscountButtonVisibility => HasDiscount ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public System.Windows.Visibility OriginalPriceVisibility => IsPriceOverridden ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
