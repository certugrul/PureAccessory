using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class EditProductPriceWindow : Window
{
    private readonly Product _product;
    private readonly ProductService _productService;
    private readonly CultureInfo _trCulture = new("tr-TR");

    public decimal NewPrice { get; private set; }

    public EditProductPriceWindow(Product product, ProductService? productService = null)
    {
        _product = product ?? throw new ArgumentNullException(nameof(product));
        _productService = productService ?? new ProductService();

        InitializeComponent();

        Loaded += EditProductPriceWindow_Loaded;
    }

    private void EditProductPriceWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            TxtProductName.Text = _product.Name;
            TxtProductCodeInfo.Text = $"Kod: {_product.Code} | Mevcut Fiyat: ₺{_product.SalePrice:N2}";
            TxtNewPrice.Text = NumberParser.FormatForEditing(_product.SalePrice);

            TxtNewPrice.Focus();
            TxtNewPrice.SelectAll();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditProductPriceWindow] Loaded error: {ex.Message}");
        }
    }

    private decimal ParsePrice(string text)
    {
        return NumberParser.ParseDecimalSafe(text);
    }

    private void AdjustPrice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && NumberParser.TryParseDecimal(btn.Tag?.ToString(), out var diff))
        {
            decimal current = ParsePrice(TxtNewPrice.Text);
            decimal next = Math.Max(0, current + diff);
            TxtNewPrice.Text = NumberParser.FormatForEditing(next);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            decimal price = ParsePrice(TxtNewPrice.Text);
            if (price < 0)
            {
                MessageBox.Show("Satış fiyatı 0'dan küçük olamaz.", "Hatalı Fiyat", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtNewPrice.Focus();
                return;
            }

            bool success = await _productService.UpdateProductPriceAsync(_product.Id, price);
            if (success)
            {
                _product.SalePrice = price;
                NewPrice = price;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Fiyat güncellenirken bir sorun oluştu.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fiyat güncellenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
