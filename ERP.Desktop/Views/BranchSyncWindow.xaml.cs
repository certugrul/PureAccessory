using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;
using Microsoft.Win32;

namespace ERP.Desktop.Views;

public partial class BranchSyncWindow : Window
{
    private readonly BranchSyncService _syncService;
    private readonly DatabaseService _dbService;
    private BranchSyncPackage? _currentPackage;
    private readonly ObservableCollection<SyncCorrectionItem> _corrections = new();

    public BranchSyncWindow(DatabaseService? dbService = null)
    {
        InitializeComponent();
        _dbService = dbService ?? new DatabaseService();
        _syncService = new BranchSyncService(_dbService);

        ListCorrections.ItemsSource = _corrections;
    }

    /// <summary>
    /// .sync veya .json dosyasından mağaza veri paketini açar
    /// </summary>
    private async void OpenPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Şube Senkronizasyon Paketini Seçin",
                Filter = "Pure Accessory Senkronizasyon Paketi (*.sync;*.json)|*.sync;*.json|Tüm Dosyalar (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtStatus.Text = "Paket okunuyor...";
                _currentPackage = await _syncService.LoadPackageFromFileAsync(dialog.FileName);
                BindPackageData(_currentPackage, Path.GetFileName(dialog.FileName));
                TxtStatus.Text = $"✅ '{Path.GetFileName(dialog.FileName)}' paketi başarıyla yüklendi.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Paket yüklenirken hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "Paket yükleme hatası.";
        }
    }

    /// <summary>
    /// Eğer ayarlanmışsa doğrudan mağaza SQL bağlantısından verileri çeker
    /// </summary>
    private async void FetchLive_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ThemeService.Current;
            string? remoteConn = settings.CustomDbConnectionString;

            if (string.IsNullOrWhiteSpace(remoteConn))
            {
                // Kullanıcıdan bağlantı adresi iste
                var inputPrompt = Microsoft.VisualBasic.Interaction.InputBox(
                    "Mağazanın SQL Server bağlantı dizesini girin:\n(Örnek: Server=192.168.1.100;Database=ERPSystem;User Id=sa;Password=...;TrustServerCertificate=True)",
                    "Canlı Mağaza Bağlantısı",
                    "");

                if (string.IsNullOrWhiteSpace(inputPrompt)) return;
                remoteConn = inputPrompt;
            }

            TxtStatus.Text = "Mağazaya bağlanılıyor ve veriler çekiliyor...";
            var remoteDb = new DatabaseService(remoteConn);
            bool ok = await remoteDb.TestConnectionAsync();
            if (!ok)
            {
                MessageBox.Show("Mağaza SQL Server'ına ulaşılamadı. IP adresini ve güvenlik duvarı izinlerini kontrol edin.", "Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtStatus.Text = "Mağaza bağlantısı başarısız.";
                return;
            }

            var remoteSync = new BranchSyncService(remoteDb);
            _currentPackage = await remoteSync.CreateSyncPackageAsync(DateTime.Today.AddDays(-7), DateTime.Today, "Canlı Mağaza (Ağ)");
            BindPackageData(_currentPackage, "Canlı Mağaza Ağ Bağlantısı");
            TxtStatus.Text = "✅ Canlı veriler başarıyla mağaza veritabanından çekildi.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Canlı veri çekme hatası:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Mağaza bilgisayarında iken mevcut verileri .sync paketi olarak dışa aktarır
    /// </summary>
    private async void CreateExportPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Senkronizasyon Paketini Kaydet",
                Filter = "Senkronizasyon Paketi (*.sync)|*.sync",
                FileName = $"PureAccessory_Magaza_{DateTime.Now:yyyyMMdd_HHmm}.sync"
            };

            if (saveDialog.ShowDialog() == true)
            {
                TxtStatus.Text = "Paket hazırlanıyor...";
                var pkg = await _syncService.CreateSyncPackageAsync(DateTime.Today.AddDays(-30), DateTime.Today, "Pure Accessory Mağaza");
                await _syncService.ExportPackageToFileAsync(pkg, saveDialog.FileName);

                MessageBox.Show($"Senkronizasyon paketi başarıyla oluşturuldu!\n\nDosya: {saveDialog.FileName}\nToplam Ciro: {pkg.FormattedTotalRevenue}\nFiş Sayısı: {pkg.SalesCount}", 
                                "Paket Hazır", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtStatus.Text = $"✅ Paket kaydedildi: {Path.GetFileName(saveDialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Paket oluşturma hatası:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Paketteki verileri KPI kartlarına ve tablolara doldurur
    /// </summary>
    private void BindPackageData(BranchSyncPackage pkg, string sourceName)
    {
        TxtTotalRevenue.Text = pkg.FormattedTotalRevenue;
        TxtTotalCash.Text = pkg.FormattedTotalCash;
        TxtTotalCard.Text = pkg.FormattedTotalCard;
        TxtTotalSalesCount.Text = $"{pkg.SalesCount} Adet";
        TxtTotalExpenses.Text = pkg.FormattedTotalExpenses;
        TxtBranchInfo.Text = $"{pkg.BranchName} ({pkg.FormattedDateRange})";

        GridSales.ItemsSource = null;
        GridSales.ItemsSource = pkg.Sales;

        GridCashTransactions.ItemsSource = null;
        GridCashTransactions.ItemsSource = pkg.CashTransactions;

        GridClosures.ItemsSource = null;
        GridClosures.ItemsSource = pkg.Closures;
    }

    private void GridSales_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridSales.SelectedItem is Sale selectedSale)
        {
            GridSaleItems.ItemsSource = selectedSale.Items;
        }
        else
        {
            GridSaleItems.ItemsSource = null;
        }
    }

    /// <summary>
    /// Seçili fişin tutarını veya ödeme tipini düzeltir
    /// </summary>
    private void EditSelectedSale_Click(object sender, RoutedEventArgs e)
    {
        if (GridSales.SelectedItem is not Sale selectedSale)
        {
            MessageBox.Show("Lütfen düzenlemek istediğiniz fişi seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newTotalStr = Microsoft.VisualBasic.Interaction.InputBox(
            $"Fiş #{selectedSale.Id} için yeni toplam tutarı girin (TL):",
            "Fiş / Satış Düzenle",
            selectedSale.GrandTotal.ToString("F2"));

        if (string.IsNullOrWhiteSpace(newTotalStr)) return;

        if (decimal.TryParse(newTotalStr.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal newTotal))
        {
            decimal diff = newTotal - selectedSale.GrandTotal;
            selectedSale.GrandTotal = newTotal;
            if (selectedSale.PaymentType == "Nakit") selectedSale.CashAmount = newTotal;
            else if (selectedSale.PaymentType == "Kredi Kartı" || selectedSale.PaymentType == "Kart") selectedSale.CardAmount = newTotal;

            // Düzeltmeyi kaydet
            _corrections.Add(new SyncCorrectionItem
            {
                TargetType = "Sale",
                OriginalId = selectedSale.Id,
                Action = "Update",
                UpdatedSale = selectedSale,
                Reason = $"Tutar güncellendi: {newTotal:N2} TL"
            });

            RecalculateTotals();
            GridSales.Items.Refresh();
            TxtStatus.Text = $"✏️ Fiş #{selectedSale.Id} güncellendi. (Yeni tutar: ₺{newTotal:N2})";
        }
        else
        {
            MessageBox.Show("Geçerli bir tutar girmediniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Seçili fişi iptal eder / paketten çıkarır
    /// </summary>
    private void DeleteSelectedSale_Click(object sender, RoutedEventArgs e)
    {
        if (GridSales.SelectedItem is not Sale selectedSale)
        {
            MessageBox.Show("Lütfen çıkarmak istediğiniz fişi seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show($"Fiş #{selectedSale.Id} ({selectedSale.FormattedTotal}) iptal edilsin / silinsin mi?\nBu işlem mağaza veritabanına da 'İptal' olarak yansıtılacaktır.",
                                      "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes && _currentPackage != null)
        {
            _currentPackage.Sales.Remove(selectedSale);

            _corrections.Add(new SyncCorrectionItem
            {
                TargetType = "Sale",
                OriginalId = selectedSale.Id,
                Action = "Delete",
                Reason = "Merkez tarafından iptal edildi"
            });

            RecalculateTotals();
            GridSales.ItemsSource = null;
            GridSales.ItemsSource = _currentPackage.Sales;
            GridSaleItems.ItemsSource = null;
            TxtStatus.Text = $"🗑️ Fiş #{selectedSale.Id} iptal edildi.";
        }
    }

    /// <summary>
    /// Seçili kasa hareketini veya masrafı düzenler
    /// </summary>
    private void EditSelectedTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (GridCashTransactions.SelectedItem is not CashTransaction selectedTx)
        {
            MessageBox.Show("Lütfen düzenlemek istediğiniz kasa hareketini seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newAmountStr = Microsoft.VisualBasic.Interaction.InputBox(
            $"İşlem #{selectedTx.Id} ({selectedTx.Type}) için yeni tutarı girin (TL):",
            "Kasa Hareketi Düzenle",
            selectedTx.Amount.ToString("F2"));

        if (string.IsNullOrWhiteSpace(newAmountStr)) return;

        if (decimal.TryParse(newAmountStr.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal newAmount))
        {
            selectedTx.Amount = newAmount;

            _corrections.Add(new SyncCorrectionItem
            {
                TargetType = "CashTransaction",
                OriginalId = selectedTx.Id,
                Action = "Update",
                UpdatedTransaction = selectedTx,
                Reason = $"Tutar güncellendi: {newAmount:N2} TL"
            });

            RecalculateTotals();
            GridCashTransactions.Items.Refresh();
            TxtStatus.Text = $"✏️ Kasa hareketi #{selectedTx.Id} güncellendi.";
        }
    }

    /// <summary>
    /// Seçili kasa hareketini çıkarır
    /// </summary>
    private void DeleteSelectedTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (GridCashTransactions.SelectedItem is not CashTransaction selectedTx)
        {
            MessageBox.Show("Lütfen çıkarmak istediğiniz işlemi seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show($"Kasa hareketi #{selectedTx.Id} ({selectedTx.Type} - {selectedTx.FormattedAmount}) silinsin mi?",
                                      "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes && _currentPackage != null)
        {
            _currentPackage.CashTransactions.Remove(selectedTx);

            _corrections.Add(new SyncCorrectionItem
            {
                TargetType = "CashTransaction",
                OriginalId = selectedTx.Id,
                Action = "Delete",
                Reason = "Merkez tarafından silindi"
            });

            RecalculateTotals();
            GridCashTransactions.ItemsSource = null;
            GridCashTransactions.ItemsSource = _currentPackage.CashTransactions;
            TxtStatus.Text = $"🗑️ Kasa hareketi #{selectedTx.Id} silindi.";
        }
    }

    private void RecalculateTotals()
    {
        if (_currentPackage == null) return;

        _currentPackage.TotalRevenue = _currentPackage.Sales.Sum(s => s.GrandTotal);
        _currentPackage.TotalCash = _currentPackage.Sales.Sum(s => s.GetEffectiveCashAmount());
        _currentPackage.TotalCard = _currentPackage.Sales.Sum(s => s.GetEffectiveCardAmount());
        _currentPackage.SalesCount = _currentPackage.Sales.Count;
        _currentPackage.TotalExpenses = _currentPackage.CashTransactions
            .Where(t => t.IsNegative)
            .Sum(t => Math.Abs(t.Amount));

        TxtTotalRevenue.Text = _currentPackage.FormattedTotalRevenue;
        TxtTotalCash.Text = _currentPackage.FormattedTotalCash;
        TxtTotalCard.Text = _currentPackage.FormattedTotalCard;
        TxtTotalSalesCount.Text = $"{_currentPackage.SalesCount} Adet";
        TxtTotalExpenses.Text = _currentPackage.FormattedTotalExpenses;
    }

    /// <summary>
    /// İncelenen verileri yerel (Merkez) SQL veritabanına aktarır
    /// </summary>
    private async void ApplyToLocalDb_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPackage == null || (_currentPackage.Sales.Count == 0 && _currentPackage.CashTransactions.Count == 0))
        {
            MessageBox.Show("Aktarılacak herhangi bir veri paketi yüklenmedi.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Bu paketteki {_currentPackage.Sales.Count} satış ve {_currentPackage.CashTransactions.Count} kasa hareketi yerel veritabanınıza (Merkeze) aktarılacak.\n\nOnaylıyor musunuz?",
            "Veritabanına Aktar", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            TxtStatus.Text = "Veritabanına aktarılıyor...";
            var result = await _syncService.ApplyPackageToDatabaseAsync(_currentPackage);

            if (result.Success)
            {
                MessageBox.Show(
                    $"{result.Message}\n\n" +
                    $"• Yeni Eklenen Satış: {result.SalesImported}\n" +
                    $"• Güncellenen Satış: {result.SalesUpdated}\n" +
                    $"• Kasa Hareketleri: {result.TransactionsImported}",
                    "Başarılı Aktarım", MessageBoxButton.OK, MessageBoxImage.Information);

                TxtStatus.Text = "✅ Veriler yerel veritabanınıza başarıyla aktarıldı.";
            }
            else
            {
                MessageBox.Show($"Aktarım sırasında hata:\n{result.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "Aktarım hatası.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Beklenmeyen hata:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Merkezde yapılan düzeltmeleri hem yerel DB'ye hem de mağaza veritabanına uygular / paketler
    /// </summary>
    private async void SendCorrectionsToStore_Click(object sender, RoutedEventArgs e)
    {
        if (_corrections.Count == 0)
        {
            MessageBox.Show("Henüz herhangi bir düzeltme (güncelleme veya silme) yapmadınız.\nFiş veya masraflarda değişiklik yaptıktan sonra bu butonu kullanabilirsiniz.",
                            "Düzeltme Yok", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Mağaza İçin Düzeltme Paketini Kaydet",
                Filter = "Düzeltme Paketi (*.sync)|*.sync",
                FileName = $"PureAccessory_Duzeltme_{DateTime.Now:yyyyMMdd_HHmm}.sync"
            };

            if (saveDialog.ShowDialog() == true)
            {
                TxtStatus.Text = "Düzeltme paketi oluşturuluyor...";
                await _syncService.ExportCorrectionPackageAsync(_corrections.ToList(), saveDialog.FileName);

                // Kendi yerel veritabanımıza da bu düzeltmeleri uygula
                await _syncService.ApplyCorrectionsAsync(_corrections.ToList());

                // Eğer canlı mağaza bağlantısı tanımlıysa oraya da direkt yazmayı teklif et
                var settings = ThemeService.Current;
                if (!string.IsNullOrWhiteSpace(settings.CustomDbConnectionString))
                {
                    var askDirect = MessageBox.Show("Kayıtlı mağaza SQL bağlantısı bulundu. Düzeltmeler doğrudan mağaza veritabanına da anında uygulansın mı?",
                                                    "Canlı Senkronizasyon", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (askDirect == MessageBoxResult.Yes)
                    {
                        int directApplied = await _syncService.ApplyCorrectionsAsync(_corrections.ToList(), settings.CustomDbConnectionString);
                        MessageBox.Show($"Düzeltmeler mağaza veritabanına doğrudan uygulandı ({directApplied} kayıt).", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                MessageBox.Show(
                    $"Düzeltmeler hem kendi veritabanınıza uygulandı hem de mağaza için düzeltme paketi kaydedildi:\n\n" +
                    $"📁 Dosya: {saveDialog.FileName}\n\n" +
                    $"Bu dosyayı mağazadaki bilgisayarda 'Ayarlar -> Merkezden Gelen Düzeltmeyi Uygula' seçeneğiyle yükleyebilirsiniz.",
                    "Çift Taraflı Senkronizasyon Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);

                TxtStatus.Text = $"✅ Düzeltme paketi oluşturuldu ({_corrections.Count} kayıt).";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Düzeltme gönderme hatası:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
