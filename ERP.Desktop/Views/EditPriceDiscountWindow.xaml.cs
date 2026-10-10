using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class EditPriceDiscountWindow : Window
{
    private readonly CartItem _item;
    private readonly ProductService _productService;
    private readonly CultureInfo _trCulture = new("tr-TR");
    private bool _isInitializing = true;

    public bool WasMasterPriceUpdated { get; private set; }

    public EditPriceDiscountWindow(CartItem item, ProductService? productService = null)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _productService = productService ?? new ProductService();
        _isInitializing = true;

        InitializeComponent();

        Loaded += EditPriceDiscountWindow_Loaded;
    }

    private void EditPriceDiscountWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        try
        {
            TxtProductName.Text = _item.Name;
            decimal originalPrice = _item.OriginalUnitPrice > 0 ? _item.OriginalUnitPrice : _item.UnitPrice;
            TxtProductCodeInfo.Text = $"Kod: {_item.Code} | Orijinal Liste Fiyatı: ₺{originalPrice:N2}";
            TxtStockInfo.Text = $"{_item.Quantity} Adet";

            TxtUnitPrice.Text = NumberParser.FormatForEditing(_item.UnitPrice);

            if (_item.DiscountPercent > 0)
            {
                RbPercent.IsChecked = true;
                TxtDiscountInput.Text = _item.DiscountPercent.ToString("0.##", _trCulture);
            }
            else if (_item.DiscountAmount > 0)
            {
                RbAmount.IsChecked = true;
                TxtDiscountInput.Text = NumberParser.FormatForEditing(_item.DiscountAmount);
            }
            else
            {
                RbPercent.IsChecked = true;
                TxtDiscountInput.Text = "0";
            }

            UpdatePresetVisibility();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditPriceDiscountWindow] Load error: {ex.Message}");
        }
        finally
        {
            _isInitializing = false;
            RecalculateSummary();
        }

        TxtUnitPrice.Focus();
        TxtUnitPrice.SelectAll();
    }

    private void DiscountType_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _item == null) return;
        UpdatePresetVisibility();
        RecalculateSummary();
    }

    private void UpdatePresetVisibility()
    {
        if (PanelPercentPresets == null || PanelAmountPresets == null) return;

        if (RbPercent?.IsChecked == true)
        {
            PanelPercentPresets.Visibility = Visibility.Visible;
            PanelAmountPresets.Visibility = Visibility.Collapsed;
        }
        else
        {
            PanelPercentPresets.Visibility = Visibility.Collapsed;
            PanelAmountPresets.Visibility = Visibility.Visible;
        }
    }

    private void PriceOrDiscount_Changed(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _item == null) return;
        RecalculateSummary();
    }

    private void RecalculateSummary()
    {
        if (_item == null) return;

        decimal unitPrice = ParseDecimal(TxtUnitPrice?.Text ?? "0");
        decimal discountInput = ParseDecimal(TxtDiscountInput?.Text ?? "0");

        if (unitPrice < 0) unitPrice = 0;
        if (discountInput < 0) discountInput = 0;

        decimal grossTotal = _item.Quantity * unitPrice;
        decimal discountAmount = 0;
        decimal discountPercent = 0;

        if (RbPercent?.IsChecked == true)
        {
            if (discountInput > 100) discountInput = 100;
            discountPercent = discountInput;
            discountAmount = Math.Round(grossTotal * (discountPercent / 100m), 2);
        }
        else
        {
            if (discountInput > grossTotal) discountInput = grossTotal;
            discountAmount = discountInput;
            discountPercent = grossTotal > 0 ? Math.Round((discountAmount / grossTotal) * 100m, 1) : 0;
        }

        decimal netTotal = Math.Max(0, grossTotal - discountAmount);

        if (TxtSummaryGross != null)
        {
            TxtSummaryGross.Text = $"₺{grossTotal:N2} ({_item.Quantity} x ₺{unitPrice:N2})";
        }

        if (TxtSummaryDiscount != null)
        {
            TxtSummaryDiscount.Text = discountAmount > 0
                ? $"-₺{discountAmount:N2} (%{discountPercent:N0})"
                : "₺0,00";
        }

        if (TxtSummaryNet != null)
        {
            TxtSummaryNet.Text = $"₺{netTotal:N2}";
        }
    }

    private decimal ParseDecimal(string text)
    {
        return NumberParser.ParseDecimalSafe(text);
    }

    private void ResetToOriginalPrice_Click(object sender, RoutedEventArgs e)
    {
        decimal orig = _item.OriginalUnitPrice > 0 ? _item.OriginalUnitPrice : _item.UnitPrice;
        TxtUnitPrice.Text = NumberParser.FormatForEditing(orig);
        RecalculateSummary();
    }

    private void AdjustPrice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && NumberParser.TryParseDecimal(btn.Tag?.ToString(), out var diff))
        {
            decimal current = ParseDecimal(TxtUnitPrice.Text);
            decimal next = Math.Max(0, current + diff);
            TxtUnitPrice.Text = NumberParser.FormatForEditing(next);
            RecalculateSummary();
        }
    }

    private void ClearDiscount_Click(object sender, RoutedEventArgs e)
    {
        TxtDiscountInput.Text = "0";
        RecalculateSummary();
    }

    private void PresetDiscountPercent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag != null)
        {
            RbPercent.IsChecked = true;
            TxtDiscountInput.Text = btn.Tag.ToString();
            RecalculateSummary();
        }
    }

    private void PresetDiscountAmount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag != null)
        {
            RbAmount.IsChecked = true;
            TxtDiscountInput.Text = btn.Tag.ToString();
            RecalculateSummary();
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            decimal unitPrice = ParseDecimal(TxtUnitPrice.Text);
            if (unitPrice < 0)
            {
                MessageBox.Show("Satış birim fiyatı 0'dan küçük olamaz.", "Hatalı Fiyat", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtUnitPrice.Focus();
                return;
            }

            decimal discountInput = ParseDecimal(TxtDiscountInput.Text);
            if (discountInput < 0) discountInput = 0;

            // 1. Sepetteki ürünün fiyatını güncelle
            _item.UnitPrice = unitPrice;

            // 2. İndirimi uygula
            if (RbPercent.IsChecked == true)
            {
                if (discountInput > 100) discountInput = 100;
                _item.DiscountPercent = discountInput;
                if (discountInput == 0) _item.DiscountAmount = 0;
            }
            else
            {
                var gross = _item.Quantity * unitPrice;
                if (discountInput > gross) discountInput = gross;
                _item.DiscountAmount = discountInput;
                if (discountInput == 0) _item.DiscountPercent = 0;
            }

            // 3. Kalıcı olarak sistem liste fiyatını güncelleme istendiyse
            if (ChkUpdateMasterPrice.IsChecked == true && _item.ProductId > 0)
            {
                var updated = await _productService.UpdateProductPriceAsync(_item.ProductId, unitPrice);
                if (updated)
                {
                    WasMasterPriceUpdated = true;
                }
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fiyat ve indirim kaydedilirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
