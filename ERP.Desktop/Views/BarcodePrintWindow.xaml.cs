using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;
using Microsoft.Win32;

namespace ERP.Desktop.Views;

public partial class BarcodePrintWindow : Window
{
    private readonly ProductService _productService;
    private readonly BarcodeService _barcodeService;
    private readonly BarcodePrintService _printService;

    public ObservableCollection<LabelPrintItem> Items { get; } = new();
    private readonly List<LabelPrintItem> _allItems = new();
    private bool _isLoaded = false;

    public BarcodePrintWindow(int? preselectProductId = null, ProductService? productService = null)
    {
        InitializeComponent();

        _productService = productService ?? new ProductService();
        _barcodeService = BarcodeService.Instance;
        _printService = new BarcodePrintService(_barcodeService);

        GridItems.ItemsSource = Items;

        Loaded += async (_, _) =>
        {
            await LoadDataAsync(preselectProductId);
            _isLoaded = true;
            UpdatePreviewUI();
            RefreshSummaryUI();
        };
    }

    private async Task LoadDataAsync(int? preselectProductId)
    {
        try
        {
            var products = await _productService.GetAllProductsAsync();
            _allItems.Clear();
            Items.Clear();

            foreach (var p in products)
            {
                var item = new LabelPrintItem
                {
                    Product = p,
                    PrintCount = 1,
                    IsSelected = (preselectProductId == null) || (p.Id == preselectProductId)
                };

                _allItems.Add(item);
                Items.Add(item);
            }

            if (preselectProductId != null)
            {
                var selected = Items.FirstOrDefault(i => i.Product.Id == preselectProductId);
                if (selected != null)
                {
                    GridItems.SelectedItem = selected;
                }
            }
            else if (Items.Count > 0)
            {
                GridItems.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ürünler yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private LabelPaperFormat GetSelectedPaperFormat()
    {
        if (CmbPaperFormat?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag switch
            {
                "A4_30Labels" => LabelPaperFormat.A4_30Labels,
                "A4_40Labels" => LabelPaperFormat.A4_40Labels,
                "Thermal_60x40" => LabelPaperFormat.Thermal_60x40,
                "Thermal_50x30" => LabelPaperFormat.Thermal_50x30,
                _ => LabelPaperFormat.A4_24Labels
            };
        }
        return LabelPaperFormat.A4_24Labels;
    }

    private BarcodePrintOptions BuildOptions()
    {
        return new BarcodePrintOptions
        {
            StoreName = TxtStoreName != null ? TxtStoreName.Text.Trim() : "ERP MARKET",
            ShowStoreName = ChkShowStoreName?.IsChecked == true,
            ShowProductName = ChkShowProductName?.IsChecked == true,
            ShowPrice = ChkShowPrice?.IsChecked == true,
            ShowBarcodeText = ChkShowBarcodeText?.IsChecked == true,
            ShowQrCode = ChkShowQrCode?.IsChecked == true,
            PaperFormat = GetSelectedPaperFormat()
        };
    }

    private void UpdatePreviewUI()
    {
        if (!_isLoaded || TxtPreviewStore == null || TxtPreviewProduct == null || ImgPreviewBarcode == null) return;

        var selectedItem = (GridItems?.SelectedItem as LabelPrintItem) ?? Items.FirstOrDefault(i => i.IsSelected) ?? Items.FirstOrDefault();
        if (selectedItem == null) return;

        var product = selectedItem.Product;
        var options = BuildOptions();

        TxtPreviewStore.Text = options.StoreName;
        TxtPreviewStore.Visibility = options.ShowStoreName ? Visibility.Visible : Visibility.Collapsed;

        TxtPreviewProduct.Text = product.Name;
        TxtPreviewProduct.Visibility = options.ShowProductName ? Visibility.Visible : Visibility.Collapsed;

        string codeToUse = !string.IsNullOrWhiteSpace(product.Barcode) ? product.Barcode : product.Code;
        TxtPreviewCode.Text = codeToUse;
        TxtPreviewCode.Visibility = options.ShowBarcodeText ? Visibility.Visible : Visibility.Collapsed;

        TxtPreviewPrice.Text = $"₺{product.SalePrice:N2}";
        TxtPreviewPrice.Visibility = options.ShowPrice ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            ImgPreviewBarcode.Source = _barcodeService.GenerateBarcodeBitmapSource(codeToUse, moduleWidth: 2, height: 50);
        }
        catch
        {
            try
            {
                ImgPreviewBarcode.Source = _barcodeService.GenerateBarcodeBitmapSource(product.Code, moduleWidth: 2, height: 50);
            }
            catch { }
        }
    }

    private void RefreshSummaryUI()
    {
        if (!_isLoaded || TxtSummarySelection == null || TxtSummaryLabels == null || TxtSummaryPages == null) return;

        int selectedProductCount = Items.Count(i => i.IsSelected);
        int totalLabels = Items.Where(i => i.IsSelected).Sum(i => i.PrintCount);

        var format = GetSelectedPaperFormat();
        int labelsPerPage = format switch
        {
            LabelPaperFormat.A4_24Labels => 24,
            LabelPaperFormat.A4_30Labels => 30,
            LabelPaperFormat.A4_40Labels => 40,
            _ => 1
        };

        int estimatedPages = (format == LabelPaperFormat.Thermal_60x40 || format == LabelPaperFormat.Thermal_50x30)
            ? totalLabels
            : (int)Math.Ceiling((double)totalLabels / labelsPerPage);

        TxtSummarySelection.Text = $"Seçilen: {selectedProductCount} / {Items.Count} Ürün";
        TxtSummaryLabels.Text = $"Toplam Basılacak Etiket: {totalLabels} Adet";
        TxtSummaryPages.Text = $"Tahmini Sayfa Sayısı: {estimatedPages} Sayfa (Şablon: {format})";
    }

    private void GridItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        UpdatePreviewUI();
    }

    private void ItemCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        RefreshSummaryUI();
    }

    private void PrintCount_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded) return;
        RefreshSummaryUI();
    }

    private void OptionsChanged_Trigger(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        UpdatePreviewUI();
        RefreshSummaryUI();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded || TxtSearch == null) return;
        string q = TxtSearch.Text.Trim();
        Items.Clear();

        var filtered = string.IsNullOrEmpty(q)
            ? _allItems
            : _allItems.Where(x =>
                x.Product.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(x.Product.Barcode) && x.Product.Barcode.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                x.Product.Code.Contains(q, StringComparison.OrdinalIgnoreCase)
              );

        foreach (var item in filtered)
        {
            Items.Add(item);
        }

        RefreshSummaryUI();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items) item.IsSelected = true;
        GridItems.Items.Refresh();
        RefreshSummaryUI();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items) item.IsSelected = false;
        GridItems.Items.Refresh();
        RefreshSummaryUI();
    }

    private void SetAllQty1_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items.Where(i => i.IsSelected)) item.PrintCount = 1;
        GridItems.Items.Refresh();
        RefreshSummaryUI();
    }

    private void SetAllQty5_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items.Where(i => i.IsSelected)) item.PrintCount = 5;
        GridItems.Items.Refresh();
        RefreshSummaryUI();
    }

    private void SetAllQtyStock_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items.Where(i => i.IsSelected))
        {
            item.PrintCount = Math.Max(1, (int)item.Product.Stock);
        }
        GridItems.Items.Refresh();
        RefreshSummaryUI();
    }

    private async void GenerateRowBarcode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is LabelPrintItem item)
        {
            var existingBarcodes = _allItems.Select(i => i.Product.Barcode).Where(b => !string.IsNullOrEmpty(b));
            string newBarcode = _barcodeService.GenerateEan13Barcode(existingBarcodes);

            item.Product.Barcode = newBarcode;
            await _productService.SaveOrUpdateProductAsync(item.Product);

            GridItems.Items.Refresh();
            UpdatePreviewUI();

            MessageBox.Show($"'{item.Product.Name}' ürününe yeni EAN-13 barkod tanımlandı:\n{newBarcode}", "Barkod Oluşturuldu", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void AutoGenerateMissingBarcodes_Click(object sender, RoutedEventArgs e)
    {
        var missing = _allItems.Where(i => string.IsNullOrWhiteSpace(i.Product.Barcode)).ToList();
        if (missing.Count == 0)
        {
            MessageBox.Show("Barkodu olmayan ürün bulunamadı. Tüm ürünlerinizin barkodu tanımlı!", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var res = MessageBox.Show(
            $"Barkodu bulunmayan {missing.Count} adet ürüne otomatik standart EAN-13 barkod oluşturulup kaydedilsin mi?",
            "Toplu Barkod Oluşturma",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res != MessageBoxResult.Yes) return;

        var existingBarcodes = new HashSet<string>(_allItems.Select(i => i.Product.Barcode).Where(b => !string.IsNullOrEmpty(b)));

        int updatedCount = 0;
        foreach (var item in missing)
        {
            string newBarcode = _barcodeService.GenerateEan13Barcode(existingBarcodes);
            existingBarcodes.Add(newBarcode);

            item.Product.Barcode = newBarcode;
            await _productService.SaveOrUpdateProductAsync(item.Product);
            updatedCount++;
        }

        GridItems.Items.Refresh();
        UpdatePreviewUI();
        MessageBox.Show($"{updatedCount} adet ürüne başarıyla benzersiz EAN-13 barkod üretildi ve sisteme kaydedildi!", "İşlem Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void GenerateAndOpenPdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = BuildOptions();
            var selectedItems = _allItems.Where(i => i.IsSelected && i.PrintCount > 0).ToList();

            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Lütfen yazdırılacak en az bir ürün seçin ve adet belirleyin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string pdfPath = _printService.GenerateBarcodeLabelsPdf(selectedItems, options);
            BarcodePrintService.OpenPdfFile(pdfPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PDF oluşturulurken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = BuildOptions();
            var selectedItems = _allItems.Where(i => i.IsSelected && i.PrintCount > 0).ToList();

            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Lütfen yazdırılacak en az bir ürün seçin ve adet belirleyin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PDF Belgesi (*.pdf)|*.pdf",
                FileName = $"Barkod_Etiketleri_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
            };

            if (dialog.ShowDialog() == true)
            {
                _printService.GenerateBarcodeLabelsPdf(selectedItems, options, dialog.FileName);
                MessageBox.Show("PDF belgesi başarıyla kaydedildi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                BarcodePrintService.OpenPdfFile(dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PDF kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
