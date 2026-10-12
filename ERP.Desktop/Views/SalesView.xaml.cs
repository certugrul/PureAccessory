using System.Collections.ObjectModel;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class SalesView : Page
{
    private readonly ProductService _productService;
    private readonly SalesService _salesService;
    private readonly CashService _cashService;
    public ObservableCollection<CartItem> Cart { get; } = new();
    public bool HasActiveCart => Cart.Count > 0;

    private readonly SolidColorBrush _greenBrush = new((Color)ColorConverter.ConvertFromString("#059669"));
    private readonly SolidColorBrush _redBrush = new((Color)ColorConverter.ConvertFromString("#DC2626"));
    private readonly PhoneScannerService _phoneService;
    private readonly CultureInfo _trCulture = new("tr-TR");
    private List<Product> _allProducts = new();
    private decimal _calculatedReceiptDiscount = 0;

    public SalesView()
    {
        InitializeComponent();
        _productService = new ProductService();
        _salesService = new SalesService();
        _cashService = new CashService();
        _phoneService = PhoneScannerService.Instance;

        CartGrid.ItemsSource = Cart;

        // Önbellekteki ürün ve kasa durumunu anında doldur
        if (AppDataCache.IsLoaded)
        {
            if (AppDataCache.Products.Count > 0)
            {
                _allProducts = new List<Product>(AppDataCache.Products);
                FilterAndDisplayProducts();
            }

            var cachedSession = AppDataCache.TodayCashSession;
            if (cachedSession != null && cachedSession.Status == "Açık")
            {
                CashStatusBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
                CashStatusBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
                CashStatusIcon.Text = "🟢";
                CashStatusTitle.Text = "GÜN AÇIK & KASA AÇIK";
                CashStatusTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                CashStatusDesc.Text = $"Gün Açılış Tarihi: {cachedSession.FormattedDate} | Başlangıç Devir Nakiti: {cachedSession.FormattedOpeningCash} | Açılış Saati: {cachedSession.FormattedOpenedAt} | Satış terminali kullanıma hazırdır.";
                CashStatusDesc.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));

                if (TxtSaleSessionDate != null) TxtSaleSessionDate.Text = cachedSession.SessionDate.ToString("dd.MM.yyyy");
                if (TxtSaleSessionInfo != null) TxtSaleSessionInfo.Text = "Günün Fiş Dökümü (Tıklayın)";
            }
        }

        Loaded += SalesView_Loaded;
        Unloaded += SalesView_Unloaded;
    }

    private async void SalesView_Loaded(object sender, RoutedEventArgs e)
    {
        _phoneService.DeviceConnected += OnPhoneDeviceConnected;
        _phoneService.DeviceDisconnected += OnPhoneDeviceDisconnected;
        _phoneService.BarcodeReceived += OnBarcodeFromPhoneReceivedAsync;
        FinancialSyncBus.FinancialDataChanged += OnFinancialDataChanged;
        UpdatePhoneBadgeUI();

        await UpdateCashStatusUIAsync();
        await LoadProductsAsync();
        ScanBox.Focus();
    }

    private void SalesView_Unloaded(object sender, RoutedEventArgs e)
    {
        _phoneService.DeviceConnected -= OnPhoneDeviceConnected;
        _phoneService.DeviceDisconnected -= OnPhoneDeviceDisconnected;
        _phoneService.BarcodeReceived -= OnBarcodeFromPhoneReceivedAsync;
        FinancialSyncBus.FinancialDataChanged -= OnFinancialDataChanged;
    }

    private async void OnFinancialDataChanged(FinancialChangeType type, object? payload)
    {
        if (type == FinancialChangeType.DayClosure || type == FinancialChangeType.CashExpense || type == FinancialChangeType.CashInflow)
        {
            await Dispatcher.InvokeAsync(async () => await UpdateCashStatusUIAsync());
        }
    }

    private void OnPhoneDeviceConnected(ConnectedPhoneDevice device)
    {
        Dispatcher.Invoke(UpdatePhoneBadgeUI);
    }

    private void OnPhoneDeviceDisconnected(ConnectedPhoneDevice device)
    {
        Dispatcher.Invoke(UpdatePhoneBadgeUI);
    }

    private void UpdatePhoneBadgeUI()
    {
        var activeDevice = _phoneService.ActiveDevice;
        if (_phoneService.ConnectedDevices.Count > 0 && activeDevice != null)
        {
            PhoneBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
            PhoneBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
            PhoneBadgeText.Text = $"🟢 {activeDevice.DeviceName} Bağlı";
            PhoneBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
        }
        else
        {
            PhoneBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
            PhoneBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FECACA"));
            PhoneBadgeText.Text = "🔴 Telefon bağlı değil";
            PhoneBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
        }
    }

    private void ConnectPhoneScanner_Click(object sender, RoutedEventArgs e)
    {
        var win = new PhoneScannerWindow { Owner = Window.GetWindow(this) };
        win.ShowDialog();
        UpdatePhoneBadgeUI();
    }

    private async Task<BarcodeScanResult> OnBarcodeFromPhoneReceivedAsync(string barcode)
    {
        if (Dispatcher.CheckAccess())
        {
            return await ProcessBarcodeCoreAsync(barcode, isFromPhone: true);
        }
        else
        {
            return await Dispatcher.InvokeAsync(async () => await ProcessBarcodeCoreAsync(barcode, isFromPhone: true)).Task.Unwrap();
        }
    }

    private async void ScanBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await ProcessScanInputAsync();
        }
    }

    private async void AddScannedProduct_Click(object sender, RoutedEventArgs e)
    {
        await ProcessScanInputAsync();
    }

    private async Task ProcessScanInputAsync()
    {
        var input = ScanBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            ScanBox.Focus();
            return;
        }

        await ProcessBarcodeCoreAsync(input, isFromPhone: false);
    }

    private async Task<BarcodeScanResult> ProcessBarcodeCoreAsync(string input, bool isFromPhone)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new BarcodeScanResult { Success = false, Barcode = input, Message = "Boş barkod" };
        }

        if (!isFromPhone)
        {
            if (!await EnsureCashRegisterOpenAsync())
            {
                return new BarcodeScanResult { Success = false, Barcode = input, Message = "Kasa kapalı" };
            }
        }

        // Önce bellekteki yüklü ürün listesinde hızlı arama yap (veritabanı kilidi ve gecikmeyi önler)
        Product? product = null;
        if (_allProducts != null && _allProducts.Count > 0)
        {
            string term = input.Trim();
            if (term.StartsWith("QR:", StringComparison.OrdinalIgnoreCase)) term = term[3..].Trim();
            else if (term.StartsWith("ERP:", StringComparison.OrdinalIgnoreCase)) term = term[4..].Trim();

            product = _allProducts.FirstOrDefault(p =>
                (!string.IsNullOrWhiteSpace(p.QrCode) && p.QrCode.Equals(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(p.Barcode) && p.Barcode.Equals(term, StringComparison.OrdinalIgnoreCase)) ||
                p.Code.Equals(term, StringComparison.OrdinalIgnoreCase))
                ?? _allProducts.FirstOrDefault(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (product == null)
        {
            product = await _productService.FindByScanAsync(input);
        }

        if (product != null)
        {
            await AddProductToCartAsync(product, isFromPhone);

            var existing = Cart.FirstOrDefault(c => c.ProductId == product.Id);
            return new BarcodeScanResult
            {
                Success = true,
                Barcode = input,
                ProductName = product.Name,
                Price = product.SalePrice,
                QuantityInCart = existing?.Quantity ?? 1,
                Message = $"{product.Name} sepete eklendi (₺{product.SalePrice:N2})"
            };
        }
        else
        {
            // Ürün bulunamadı
            string sourceTag = isFromPhone ? "📱 Telefondan okunan" : "";
            ScanFeedbackText.Text = $"⚠️ {sourceTag} '{input}' koduna ait ürün bulunamadı! Lütfen QR / Barkod kontrol edin.";
            ScanFeedbackText.Foreground = _redBrush;

            SoundService.PlayWarning();

            if (!isFromPhone)
            {
                ScanBox.SelectAll();
                ScanBox.Focus();
            }

            return new BarcodeScanResult
            {
                Success = false,
                Barcode = input,
                Message = $"'{input}' kodlu ürün bulunamadı!"
            };
        }
    }

    private async Task AddProductToCartAsync(Product product, bool isFromPhone = false)
    {
        if (!isFromPhone)
        {
            if (!await EnsureCashRegisterOpenAsync())
            {
                return;
            }
        }

        // Sepete doğrudan altına yeni satır olarak ekle
        var newItem = new CartItem
        {
            ProductId = product.Id,
            Code = product.Code,
            Name = product.Name,
            Barcode = product.Barcode,
            QrCode = product.QrCode,
            UnitPrice = product.SalePrice,
            OriginalUnitPrice = product.SalePrice,
            UnitCost = product.PurchasePrice,
            VatRate = product.VatRate,
            AvailableStock = product.Stock,
            Quantity = 1
        };

        Cart.Add(newItem);
        CartGrid.SelectedItem = newItem;
        CartGrid.ScrollIntoView(newItem);

        string sourceTag = isFromPhone ? "📱 Telefondan" : "✅";
        ScanFeedbackText.Text = $"{sourceTag} {product.Name} sepete eklendi (Fiyat: ₺{product.SalePrice:N2})";
        ScanFeedbackText.Foreground = _greenBrush;

        SoundService.PlaySuccess();

        RecalculateReceiptDiscount();
        UpdateCartSummary();

        if (!isFromPhone)
        {
            ScanBox.Clear();
            ScanBox.Focus();
        }
    }

    private async Task LoadProductsAsync()
    {
        try
        {
            _allProducts = await _productService.GetAllProductsAsync();
            FilterAndDisplayProducts();
        }
        catch { }
    }

    private void FilterAndDisplayProducts()
    {
        var query = TxtProductSearch?.Text?.Trim().ToLower() ?? "";
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allProducts
            : _allProducts.Where(p =>
                (p.Name != null && p.Name.ToLower().Contains(query)) ||
                (p.Code != null && p.Code.ToLower().Contains(query)) ||
                (!string.IsNullOrEmpty(p.Barcode) && p.Barcode.ToLower().Contains(query))
            ).ToList();

        if (ProductsItemsControl != null)
        {
            ProductsItemsControl.ItemsSource = filtered;
        }

        if (ProductCountBadge != null)
        {
            ProductCountBadge.Text = $"{filtered.Count} Ürün";
        }
    }

    private void ProductSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterAndDisplayProducts();
    }

    private void ClearProductSearch_Click(object sender, RoutedEventArgs e)
    {
        if (TxtProductSearch != null)
        {
            TxtProductSearch.Text = string.Empty;
            TxtProductSearch.Focus();
        }
    }

    private async void RefreshProducts_Click(object sender, RoutedEventArgs e)
    {
        await LoadProductsAsync();
    }

    private async void ProductCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is Product product)
        {
            await AddProductToCartAsync(product, isFromPhone: false);
        }
    }

    private void ToggleProductMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ProductMenuBorder.Visibility == Visibility.Visible)
        {
            CloseProductMenu();
        }
        else
        {
            OpenProductMenu();
        }
    }

    private void CloseProductMenu_Click(object sender, RoutedEventArgs e)
    {
        CloseProductMenu();
    }

    private void OpenProductMenu()
    {
        ProductMenuColumn.Width = new GridLength(450);
        ProductMenuBorder.Visibility = Visibility.Visible;
        if (BtnToggleProductMenu != null)
        {
            BtnToggleProductMenu.Content = "✕ Menüyü Gizle";
            BtnToggleProductMenu.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        }
        FilterAndDisplayProducts();
        TxtProductSearch?.Focus();
    }

    private void CloseProductMenu()
    {
        ProductMenuColumn.Width = new GridLength(0);
        ProductMenuBorder.Visibility = Visibility.Collapsed;
        if (BtnToggleProductMenu != null)
        {
            BtnToggleProductMenu.Content = "🛍️ Ürün Menüsü (Aç)";
            BtnToggleProductMenu.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4F46E5"));
        }
        ScanBox?.Focus();
    }

    #region Fiyat Değiştirme & İndirim İşlemleri

    // 1. Sepetteki Ürünün Fiyatını veya İndirimini Düzenle
    private async void EditCartItemPrice_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement el && el.DataContext is CartItem item)
            {
                await OpenPriceDiscountDialogForItemAsync(item);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fiyat düzenleme penceresi açılırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CartGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                var row = FindVisualParent<DataGridRow>(dep);
                if (row != null && row.DataContext is CartItem item)
                {
                    await OpenPriceDiscountDialogForItemAsync(item);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CartGrid_MouseDoubleClick error: {ex.Message}");
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T typed) return typed;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private async Task OpenPriceDiscountDialogForItemAsync(CartItem item)
    {
        if (!AuthService.CanManagePrices && !AuthService.CanApplyDiscount && !AuthService.IsAdmin)
        {
            MessageBox.Show("Fiyat düzenleme veya iskonto uygulama yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var win = new EditPriceDiscountWindow(item, _productService);
            var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (owner != null && owner != win)
            {
                win.Owner = owner;
            }

            if (win.ShowDialog() == true)
            {
                RecalculateReceiptDiscount();
                UpdateCartSummary();

                if (win.WasMasterPriceUpdated)
                {
                    await LoadProductsAsync();
                }

                ScanFeedbackText.Text = $"✏️ '{item.Name}' fiyatı ₺{item.UnitPrice:N2}" +
                    (item.HasDiscount ? $" (İndirim: ₺{item.DiscountAmount:N2})" : "") + " olarak güncellendi.";
                ScanFeedbackText.Foreground = _greenBrush;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fiyat düzenleme sırasında hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ContextMenuEditPrice_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CartGrid.SelectedItem is CartItem item)
            {
                await OpenPriceDiscountDialogForItemAsync(item);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fiyat düzenleme açılırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ContextMenuClearDiscount_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CartGrid.SelectedItem is CartItem item)
            {
                item.DiscountAmount = 0;
                item.DiscountPercent = 0;
                RecalculateReceiptDiscount();
                UpdateCartSummary();
                ScanFeedbackText.Text = $"'{item.Name}' indirimi sıfırlandı.";
                ScanFeedbackText.Foreground = _greenBrush;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ContextMenuClearDiscount error: {ex.Message}");
        }
    }

    private void ContextMenuRemoveItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CartGrid.SelectedItem is CartItem item)
            {
                Cart.Remove(item);
                RecalculateReceiptDiscount();
                UpdateCartSummary();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ContextMenuRemoveItem error: {ex.Message}");
        }
    }

    // 2. Ürün Menüsündeki Karttan Doğrudan Satış Fiyatını Güncelle
    private async void EditProductCardPrice_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (sender is FrameworkElement el && el.DataContext is Product product)
            {
                var win = new EditProductPriceWindow(product, _productService);
                var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
                if (owner != null && owner != win)
                {
                    win.Owner = owner;
                }

                if (win.ShowDialog() == true)
                {
                    await LoadProductsAsync();
                    ScanFeedbackText.Text = $"✅ '{product.Name}' ürününün liste satış fiyatı ₺{win.NewPrice:N2} olarak güncellendi.";
                    ScanFeedbackText.Foreground = _greenBrush;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ürün satış fiyatı düzenleme penceresi açılırken hata:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 3. Genel Fiş İndirimi
    private void ReceiptDiscountType_Changed(object sender, RoutedEventArgs e)
    {
        RecalculateReceiptDiscount();
    }

    private void ReceiptDiscount_TextChanged(object sender, TextChangedEventArgs e)
    {
        RecalculateReceiptDiscount();
    }

    private void ClearReceiptDiscount_Click(object sender, RoutedEventArgs e)
    {
        if (TxtReceiptDiscount != null)
        {
            TxtReceiptDiscount.Text = string.Empty;
        }
        _calculatedReceiptDiscount = 0;
        UpdateCartSummary();
    }

    private void QuickReceiptDiscount_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.CanApplyDiscount && !AuthService.IsAdmin)
        {
            MessageBox.Show("İskonto ve indirim uygulama yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is Button btn && btn.Tag != null)
        {
            string tag = btn.Tag.ToString() ?? "";
            if (tag.StartsWith("%"))
            {
                if (RbReceiptDiscPercent != null) RbReceiptDiscPercent.IsChecked = true;
                if (TxtReceiptDiscount != null) TxtReceiptDiscount.Text = tag.TrimStart('%');
            }
            else
            {
                if (RbReceiptDiscAmount != null) RbReceiptDiscAmount.IsChecked = true;
                if (TxtReceiptDiscount != null) TxtReceiptDiscount.Text = tag;
            }
            RecalculateReceiptDiscount();
        }
    }

    private void RecalculateReceiptDiscount()
    {
        if (TxtReceiptDiscount == null) return;
        decimal val = NumberParser.ParseDecimalSafe(TxtReceiptDiscount.Text);

        decimal itemsNetTotal = Cart.Sum(x => x.Total);

        if (RbReceiptDiscPercent?.IsChecked == true)
        {
            if (val > 100) val = 100;
            _calculatedReceiptDiscount = Math.Round(itemsNetTotal * (val / 100m), 2);
        }
        else
        {
            if (val > itemsNetTotal) val = itemsNetTotal;
            _calculatedReceiptDiscount = Math.Max(0, val);
        }

        UpdateCartSummary();
    }

    #endregion

    private void IncreaseQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CartItem item)
        {
            item.Quantity++;
            CartGrid.Items.Refresh();
            RecalculateReceiptDiscount();
            UpdateCartSummary();
        }
    }

    private void DecreaseQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CartItem item)
        {
            if (item.Quantity > 1)
            {
                item.Quantity--;
                CartGrid.Items.Refresh();
                RecalculateReceiptDiscount();
                UpdateCartSummary();
            }
            else
            {
                Cart.Remove(item);
                RecalculateReceiptDiscount();
                UpdateCartSummary();
            }
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CartItem item)
        {
            Cart.Remove(item);
            RecalculateReceiptDiscount();
            UpdateCartSummary();
        }
    }

    private void CancelReceipt_Click(object sender, RoutedEventArgs e)
    {
        CancelReceipt(promptUser: true);
    }

    private void ClearCart_Click(object sender, RoutedEventArgs e) => CancelReceipt_Click(sender, e);

    public bool CancelReceipt(bool promptUser = true)
    {
        if (promptUser && !AuthService.CanCancelReceipt && !AuthService.IsAdmin)
        {
            MessageBox.Show("Fiş veya satış iptali yapma yetkiniz bulunmamaktadır!\nLütfen yöneticinize başvurunuz.", "Yetki Yetersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (Cart.Count == 0)
        {
            if (promptUser)
            {
                MessageBox.Show("İptal edilecek açık fiş veya sepet ürünü bulunmuyor.", "Sepet Boş", MessageBoxButton.OK, MessageBoxImage.Information);
                ScanBox.Focus();
            }
            return true;
        }

        if (promptUser)
        {
            var totalQty = Cart.Sum(x => x.Quantity);
            var totalAmount = Cart.Sum(x => x.Total);
            var res = MessageBox.Show(
                $"Açık fişteki {Cart.Count} çeşit ({totalQty} adet, ₺{totalAmount:N2}) ürünü iptal etmek istediğinize emin misiniz?\n\n" +
                "Bu işlem fişi ve sepetteki tüm ürünleri iptal edecektir.",
                "Fiş İptali Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes)
            {
                ScanBox.Focus();
                return false;
            }
        }

        Cart.Clear();
        if (TxtReceiptDiscount != null) TxtReceiptDiscount.Text = string.Empty;
        if (TxtSplitCash != null) TxtSplitCash.Text = string.Empty;
        if (TxtSplitCard != null) TxtSplitCard.Text = string.Empty;
        _calculatedReceiptDiscount = 0;
        UpdateCartSummary();

        ScanFeedbackText.Text = "❌ Fiş iptal edildi.";
        ScanFeedbackText.Foreground = _redBrush;
        ScanBox.Text = string.Empty;
        ScanBox.Focus();

        SoundService.PlayWarning();

        return true;
    }

    private void SalesView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            CancelReceipt(promptUser: true);
        }
        else if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            _ = OpenDailyReceiptBreakdownAsync();
        }
    }

    private void CartGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && CartGrid.SelectedItem is CartItem item)
        {
            e.Handled = true;
            Cart.Remove(item);
            RecalculateReceiptDiscount();
            UpdateCartSummary();
            ScanFeedbackText.Text = $"'{item.Name}' sepetten kaldırıldı.";
            ScanFeedbackText.Foreground = _redBrush;
        }
    }

    private void UpdateCartSummary()
    {
        decimal grossSubTotal = 0;
        decimal lineDiscounts = 0;
        decimal itemsNetTotal = 0;
        int totalItems = 0;
        int totalQuantity = 0;

        foreach (var item in Cart)
        {
            grossSubTotal += item.GrossTotal;
            lineDiscounts += item.DiscountAmount;
            itemsNetTotal += item.Total;
            totalItems++;
            totalQuantity += item.Quantity;
        }

        decimal receiptDiscount = _calculatedReceiptDiscount;
        if (receiptDiscount > itemsNetTotal) receiptDiscount = itemsNetTotal;

        decimal totalDiscount = lineDiscounts + receiptDiscount;
        decimal finalGrandTotal = Math.Max(0, itemsNetTotal - receiptDiscount);

        // KDV ve net matrah hesabı
        decimal receiptRatio = itemsNetTotal > 0 ? (receiptDiscount / itemsNetTotal) : 0m;
        decimal subTotal = 0;
        decimal vatTotal = 0;

        foreach (var item in Cart)
        {
            var netLine = Math.Max(0, item.Total * (1m - receiptRatio));
            var vatFactor = item.VatRate / (100m + item.VatRate);
            var itemVat = netLine * vatFactor;
            subTotal += (netLine - itemVat);
            vatTotal += itemVat;
        }

        if (SubTotalText != null) SubTotalText.Text = $"₺{grossSubTotal:N2}";

        if (RowLineDiscounts != null)
        {
            RowLineDiscounts.Visibility = lineDiscounts > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (LineDiscountsText != null)
        {
            LineDiscountsText.Text = $"-₺{lineDiscounts:N2}";
        }

        if (RowReceiptDiscount != null)
        {
            RowReceiptDiscount.Visibility = receiptDiscount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ReceiptDiscountText != null)
        {
            ReceiptDiscountText.Text = $"-₺{receiptDiscount:N2}";
        }

        _currentGrandTotal = finalGrandTotal;

        if (VatTotalText != null) VatTotalText.Text = $"₺{vatTotal:N2}";
        if (GrandTotalText != null) GrandTotalText.Text = $"₺{finalGrandTotal:N2}";

        if (CartCountBadge != null)
        {
            CartCountBadge.Text = totalItems == 0 ? "0 Kalem" : $"{totalItems} Kalem ({totalQuantity} Adet)";
        }

        if (RbSplit?.IsChecked == true)
        {
            UpdateSplitPaymentStatusUI();
        }
    }

    #region Parçalı Ödeme İşlemleri

    private decimal _currentGrandTotal = 0;
    private bool _isUpdatingSplit = false;

    private void PaymentType_Changed(object sender, RoutedEventArgs e)
    {
        if (PanelSplitPayment == null) return;

        if (RbSplit?.IsChecked == true)
        {
            PanelSplitPayment.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(TxtSplitCash?.Text) && string.IsNullOrWhiteSpace(TxtSplitCard?.Text))
            {
                SplitFiftyFifty_Click(sender, e);
            }
            else
            {
                UpdateSplitPaymentStatusUI();
            }
        }
        else
        {
            PanelSplitPayment.Visibility = Visibility.Collapsed;
        }
    }

    private void SplitAmounts_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingSplit) return;
        UpdateSplitPaymentStatusUI();
    }

    private void SplitFiftyFifty_Click(object sender, RoutedEventArgs e)
    {
        _isUpdatingSplit = true;
        try
        {
            decimal half = Math.Round(_currentGrandTotal / 2m, 2);
            decimal otherHalf = Math.Max(0, _currentGrandTotal - half);
            if (TxtSplitCash != null) TxtSplitCash.Text = half.ToString("N2", _trCulture);
            if (TxtSplitCard != null) TxtSplitCard.Text = otherHalf.ToString("N2", _trCulture);
        }
        finally
        {
            _isUpdatingSplit = false;
            UpdateSplitPaymentStatusUI();
        }
    }

    private void SplitFillCash_Click(object sender, RoutedEventArgs e)
    {
        _isUpdatingSplit = true;
        try
        {
            decimal card = ParseDecimalSafe(TxtSplitCard?.Text);
            decimal remain = Math.Max(0, _currentGrandTotal - card);
            if (TxtSplitCash != null) TxtSplitCash.Text = remain.ToString("N2", _trCulture);
        }
        finally
        {
            _isUpdatingSplit = false;
            UpdateSplitPaymentStatusUI();
        }
    }

    private void SplitFillCard_Click(object sender, RoutedEventArgs e)
    {
        _isUpdatingSplit = true;
        try
        {
            decimal cash = ParseDecimalSafe(TxtSplitCash?.Text);
            decimal remain = Math.Max(0, _currentGrandTotal - cash);
            if (TxtSplitCard != null) TxtSplitCard.Text = remain.ToString("N2", _trCulture);
        }
        finally
        {
            _isUpdatingSplit = false;
            UpdateSplitPaymentStatusUI();
        }
    }

    private void UpdateSplitPaymentStatusUI()
    {
        if (BadgeSplitStatus == null || TxtSplitStatus == null) return;

        decimal cash = ParseDecimalSafe(TxtSplitCash?.Text);
        decimal card = ParseDecimalSafe(TxtSplitCard?.Text);
        decimal sum = cash + card;
        decimal diff = _currentGrandTotal - sum;

        if (Math.Abs(diff) < 0.01m)
        {
            BadgeSplitStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7"));
            BadgeSplitStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86EFAC"));
            TxtSplitStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#166534"));
            TxtSplitStatus.Text = $"✅ Ödeme Dengelendi (₺{cash:N2} Nakit + ₺{card:N2} Kart)";
        }
        else if (diff > 0)
        {
            BadgeSplitStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
            BadgeSplitStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
            TxtSplitStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E"));
            TxtSplitStatus.Text = $"⚠️ Kalan / Eksik: ₺{diff:N2} (Toplam: ₺{_currentGrandTotal:N2})";
        }
        else
        {
            decimal excess = Math.Abs(diff);
            BadgeSplitStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));
            BadgeSplitStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FECACA"));
            TxtSplitStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#991B1B"));
            TxtSplitStatus.Text = $"⚠️ Fazla Girildi: +₺{excess:N2} (Toplam: ₺{_currentGrandTotal:N2})";
        }
    }

    private decimal ParseDecimalSafe(string? text)
    {
        return NumberParser.ParseDecimalSafe(text);
    }

    #endregion

    private bool _isCompletingSale = false;
    private DateTime _lastSaleClickTime = DateTime.MinValue;

    private async void CompleteSale_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompletingSale || (DateTime.Now - _lastSaleClickTime).TotalMilliseconds < 2000) return;
        _lastSaleClickTime = DateTime.Now;
        _isCompletingSale = true;
        if (BtnCompleteSale != null)
        {
            BtnCompleteSale.IsEnabled = false;
            BtnCompleteSale.Content = "⏳ İşleniyor...";
        }

        List<CartItem>? cartSnapshot = null;
        decimal snapshotDiscount = 0;

        try
        {
            if (!await EnsureCashRegisterOpenAsync())
            {
                return;
            }

            if (Cart.Count == 0)
            {
                MessageBox.Show("Sepette ürün bulunmuyor. Lütfen önce ürün menüsünden tıklayarak veya QR kod/barkod okutarak ürün ekleyin.", "Sepet Boş", MessageBoxButton.OK, MessageBoxImage.Warning);
                ScanBox.Focus();
                return;
            }

            string paymentType = "Nakit";
            decimal splitCash = 0m;
            decimal splitCard = 0m;

            if (RbCreditCard.IsChecked == true)
            {
                paymentType = "Kredi Kartı";
            }
            else if (RbSplit.IsChecked == true)
            {
                paymentType = "Parçalı";
                splitCash = ParseDecimalSafe(TxtSplitCash?.Text);
                splitCard = ParseDecimalSafe(TxtSplitCard?.Text);
                decimal sum = splitCash + splitCard;
                decimal diff = Math.Abs(_currentGrandTotal - sum);

                if (diff >= 0.01m)
                {
                    var remaining = _currentGrandTotal - sum;
                    string msg = remaining > 0
                        ? $"Parçalı ödeme tutarları genel toplam ile eşleşmiyor!\n\n" +
                          $"• Genel Toplam        : ₺{_currentGrandTotal:N2}\n" +
                          $"• Girilen Nakit       : ₺{splitCash:N2}\n" +
                          $"• Girilen Kredi Kartı : ₺{splitCard:N2}\n" +
                          $"• Kalan Eksik Tutar   : ₺{remaining:N2}\n\n" +
                          $"Lütfen tutarları kontrol ediniz."
                        : $"Girilen parçalı ödeme tutarı genel toplamdan fazladır!\n\n" +
                          $"• Genel Toplam   : ₺{_currentGrandTotal:N2}\n" +
                          $"• Girilen Toplam : ₺{sum:N2}\n" +
                          $"• Fazlalık       : ₺{Math.Abs(remaining):N2}\n\n" +
                          $"Lütfen tutarları kontrol ediniz.";

                    MessageBox.Show(msg, "Parçalı Ödeme Uyuşmazlığı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtSplitCash?.Focus();
                    return;
                }
            }
            else if (RbOnAccount.IsChecked == true)
            {
                paymentType = "Veresiye";
            }

            // Satış tarihi DOĞRUDAN gün açılışından alınır (kullanıcı değiştiremez)
            var session = await _cashService.GetTodaySessionAsync(DateTime.Today) ?? await _cashService.GetActiveSessionAsync();
            if (session == null || session.Status != "Açık")
            {
                MessageBox.Show("Gün henüz açılmadı ve kasa kapalı! Satış yapabilmek için önce gün açılışı yapmalısınız.", "Kasa Kapalı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sessionDate = session.SessionDate.Date;
            var saleDateTime = new DateTime(
                sessionDate.Year, sessionDate.Month, sessionDate.Day,
                DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second
            );

            // Mükerrer satışları önlemek için sepetin kopyasını alıp UI sepetini hemen boşaltıyoruz
            cartSnapshot = Cart.ToList();
            snapshotDiscount = _calculatedReceiptDiscount;

            Cart.Clear();
            if (TxtReceiptDiscount != null) TxtReceiptDiscount.Text = string.Empty;
            if (TxtSplitCash != null) TxtSplitCash.Text = string.Empty;
            if (TxtSplitCard != null) TxtSplitCard.Text = string.Empty;
            _calculatedReceiptDiscount = 0;
            UpdateCartSummary();

            var sale = await _salesService.CompleteSaleAsync(cartSnapshot, paymentType, saleDateTime, snapshotDiscount, splitCash, splitCard);

            string paymentDisplay = sale.PaymentType == "Parçalı"
                ? $"Parçalı (₺{sale.CashAmount:N2} Nakit + ₺{sale.CardAmount:N2} Kredi Kartı)"
                : sale.PaymentType;

            var receiptInfo =
                $"🧾 SATIŞ BAŞARIYLA TAMAMLANDI\n" +
                $"----------------------------------------\n" +
                $"• Satış/Fiş No : #{sale.Id}\n" +
                $"• İşlem Tarihi : {sale.FormattedDate}\n" +
                $"• Ürün Adedi   : {sale.Items.Sum(x => x.Quantity):N0} Adet ({sale.Items.Count} Çeşit)\n" +
                $"• Ödeme Türü   : {paymentDisplay}\n" +
                (sale.DiscountTotal > 0 ? $"• Toplam İndirim: -₺{sale.DiscountTotal:N2}\n" : "") +
                $"• Toplam Tutar : ₺{sale.GrandTotal:N2}\n" +
                $"----------------------------------------\n" +
                $"Satış gün açılışı tarihine ({sessionDate:dd.MM.yyyy}) işlendi ve stoklar düşüldü.";

            MessageBox.Show(receiptInfo, "Satış Kaydedildi", MessageBoxButton.OK, MessageBoxImage.Information);

            // 1. Ses Efekti Çal
            SoundService.PlaySuccess();

            // 2. Otomatik Fiş Yazdırma Tercihi Aktifse Termal PDF Fişi Üret ve Aç
            if (ThemeService.Current?.AutoPrintReceipt == true)
            {
                try
                {
                    var pdfReportService = new PdfReportService();
                    var receiptPdf = pdfReportService.GenerateSingleSaleReceiptPdf(sale);
                    PdfReportService.OpenPdfFile(receiptPdf);
                }
                catch { }
            }

            ScanFeedbackText.Text = $"✅ Son Satış (Fiş #{sale.Id} - ₺{sale.GrandTotal:N2} [{paymentDisplay}]) başarıyla kaydedildi.";
            ScanFeedbackText.Foreground = _greenBrush;
            ScanBox.Focus();

            // Ürün stoklarını menüde hemen güncelle
            await LoadProductsAsync();
        }
        catch (Exception ex)
        {
            // Beklenmeyen hata oluştuysa ve satış tamamlanamadıysa sepeti geri yükle
            if (cartSnapshot != null && Cart.Count == 0)
            {
                foreach (var item in cartSnapshot) Cart.Add(item);
                _calculatedReceiptDiscount = snapshotDiscount;
                UpdateCartSummary();
            }
            MessageBox.Show($"Satış kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isCompletingSale = false;
            if (BtnCompleteSale != null)
            {
                BtnCompleteSale.IsEnabled = true;
                BtnCompleteSale.Content = "✅ Satışı Tamamla";
            }
        }
    }

    private async Task<bool> EnsureCashRegisterOpenAsync()
    {
        if (AppDataCache.TodayCashSession != null && AppDataCache.TodayCashSession.Status == "Açık")
        {
            return true;
        }

        var session = await _cashService.GetTodaySessionAsync() ?? await _cashService.GetActiveSessionAsync();
        if (session != null && session.Status == "Açık")
        {
            AppDataCache.UpdateTodayCashSession(session);
            return true;
        }

        MessageBox.Show(
            "Gün henüz açılmadı ve kasa kapalıdır!\n\nSatış yapabilmek ve ürün ekleyebilmek için lütfen sol menüdeki 'KASA' bölümüne giderek gün açılışı yapınız.",
            "Kasa Kapalı",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        ScanFeedbackText.Text = "⚠️ Gün açılışı (kasa açılışı) yapılmadan satış yapılamaz! Lütfen Kasa menüsünden açınız.";
        ScanFeedbackText.Foreground = _redBrush;
        return false;
    }

    private async Task UpdateCashStatusUIAsync()
    {
        try
        {
            var session = await _cashService.GetTodaySessionAsync() ?? await _cashService.GetActiveSessionAsync();
            if (session != null && session.Status == "Açık")
            {
                CashStatusBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
                CashStatusBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
                CashStatusIcon.Text = "🟢";
                CashStatusTitle.Text = "GÜN AÇIK & KASA AÇIK";
                CashStatusTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                CashStatusDesc.Text = $"Gün Açılış Tarihi: {session.FormattedDate} | Başlangıç Devir Nakiti: {session.FormattedOpeningCash} | Açılış Saati: {session.FormattedOpenedAt} | Satış terminali kullanıma hazırdır.";
                CashStatusDesc.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));

                if (TxtSaleSessionDate != null)
                {
                    TxtSaleSessionDate.Text = session.SessionDate.ToString("dd.MM.yyyy");
                }
                if (TxtSaleSessionInfo != null)
                {
                    TxtSaleSessionInfo.Text = "Günün Fiş Dökümü (Tıklayın)";
                }
            }
            else
            {
                CashStatusBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                CashStatusBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FECACA"));
                CashStatusIcon.Text = "🔴";
                CashStatusTitle.Text = "GÜN KAPALI / KASA AÇILIŞI YAPILMADI";
                CashStatusTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                CashStatusDesc.Text = "Satış yapabilmek ve ürün okutabilmek için önce gün açılışı yapmalı ve başlangıç nakitini belirlemelisiniz.";
                CashStatusDesc.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F1D1D"));

                if (TxtSaleSessionDate != null)
                {
                    TxtSaleSessionDate.Text = "Kasa Kapalı";
                }
                if (TxtSaleSessionInfo != null)
                {
                    TxtSaleSessionInfo.Text = "Fiş Dökümünü İncele";
                }
            }
        }
        catch { }
    }

    private async void SaleSessionDate_Click(object sender, MouseButtonEventArgs e)
    {
        await OpenDailyReceiptBreakdownAsync();
    }

    private async Task OpenDailyReceiptBreakdownAsync()
    {
        DateTime targetDate = DateTime.Today;
        try
        {
            var session = await _cashService.GetTodaySessionAsync(DateTime.Today) ?? await _cashService.GetActiveSessionAsync();
            if (session != null)
            {
                targetDate = session.SessionDate.Date;
            }
        }
        catch { }

        var win = new DailyReceiptBreakdownWindow(targetDate)
        {
            Owner = Window.GetWindow(this)
        };
        win.ShowDialog();
    }

    private async void OpenCashFromBanner_Click(object sender, RoutedEventArgs e)
    {
        var win = new OpenCashWindow { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            await UpdateCashStatusUIAsync();
            ScanBox.Focus();
        }
    }
}
