using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class PhoneScannerWindow : Window
{
    private readonly PhoneScannerService _service;

    public PhoneScannerWindow()
    {
        InitializeComponent();
        _service = PhoneScannerService.Instance;

        Loaded += PhoneScannerWindow_Loaded;
        Closed += PhoneScannerWindow_Closed;
    }

    private bool _isInitializingAdapters = false;

    private async void PhoneScannerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _service.DeviceConnected += OnDeviceConnected;
        _service.DeviceDisconnected += OnDeviceDisconnected;

        if (!_service.IsRunning)
        {
            await _service.StartServerAsync();
        }

        PopulateNetworkAdapters();
        RefreshServerInfoUI();
    }

    private void PopulateNetworkAdapters()
    {
        _isInitializingAdapters = true;
        try
        {
            var ips = PhoneScannerService.GetAllAvailableLocalIps();
            CmbNetworkAdapter.Items.Clear();

            ComboBoxItem? selectedItem = null;
            foreach (var (ip, desc) in ips)
            {
                var item = new ComboBoxItem
                {
                    Content = desc,
                    Tag = ip,
                    FontSize = 12
                };
                CmbNetworkAdapter.Items.Add(item);

                if (string.Equals(ip, _service.LocalIp, StringComparison.OrdinalIgnoreCase))
                {
                    selectedItem = item;
                }
            }

            if (selectedItem != null)
            {
                selectedItem.IsSelected = true;
            }
            else if (CmbNetworkAdapter.Items.Count > 0)
            {
                var first = (ComboBoxItem)CmbNetworkAdapter.Items[0];
                first.IsSelected = true;
                if (first.Tag is string ip)
                {
                    _service.SetLocalIp(ip);
                }
            }
        }
        finally
        {
            _isInitializingAdapters = false;
        }
    }

    private void CmbNetworkAdapter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingAdapters) return;

        if (CmbNetworkAdapter.SelectedItem is ComboBoxItem item && item.Tag is string ip)
        {
            _service.SetLocalIp(ip);
            RefreshServerInfoUI();
        }
    }

    private void PhoneScannerWindow_Closed(object? sender, EventArgs e)
    {
        _service.DeviceConnected -= OnDeviceConnected;
        _service.DeviceDisconnected -= OnDeviceDisconnected;
    }

    private void RefreshServerInfoUI()
    {
        TxtServerUrl.Text = _service.ActiveUrl;
        TxtTokenBadge.Text = $"Güvenlik Tokenı: {_service.SecurityToken}";

        if (_service.IsTunnelActive)
        {
            HttpsStatusCard.Visibility = Visibility.Visible;
            BtnToggleHttps.Content = "🛑 HTTPS Tünelini Kapat (Yerel Ağa Dön)";
            BtnToggleHttps.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
        }
        else
        {
            HttpsStatusCard.Visibility = Visibility.Collapsed;
            BtnToggleHttps.Content = "🌐 iPhone Canlı Kamera İçin HTTPS Başlat";
            BtnToggleHttps.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
        }

        try
        {
            ImgQrCode.Source = _service.GenerateQrCodeBitmap(_service.ActiveUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"QR Kod oluşturulamadı: {ex.Message}", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        UpdateDeviceStatusUI();
    }

    private void UpdateDeviceStatusUI()
    {
        Dispatcher.Invoke(() =>
        {
            var device = _service.ActiveDevice;
            if (device != null)
            {
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
                StatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
                StatusDot.Text = "🟢";
                StatusText.Text = $"Telefon Bağlandı: {device.DeviceName}";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));

                DeviceDetailsPanel.Visibility = Visibility.Visible;
                TxtNoDevicePrompt.Visibility = Visibility.Collapsed;

                TxtConnectedDeviceName.Text = device.DeviceName;
                TxtConnectedDeviceInfo.Text = $"IP: {device.IpAddress} | Bağlantı: {device.FormattedConnectedTime}";
                TxtScanCount.Text = $"Okutulan Barkod: {device.ScanCount} Adet";
            }
            else
            {
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                StatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FECACA"));
                StatusDot.Text = "🔴";
                StatusText.Text = "Telefon Bekleniyor...";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));

                DeviceDetailsPanel.Visibility = Visibility.Collapsed;
                TxtNoDevicePrompt.Visibility = Visibility.Visible;
            }
        });
    }

    private void OnDeviceConnected(ConnectedPhoneDevice device)
    {
        UpdateDeviceStatusUI();
    }

    private void OnDeviceDisconnected(ConnectedPhoneDevice device)
    {
        UpdateDeviceStatusUI();
    }

    private async void ToggleHttps_Click(object sender, RoutedEventArgs e)
    {
        if (_service.IsTunnelActive)
        {
            _service.StopCloudflareTunnel();
            RefreshServerInfoUI();
            return;
        }

        BtnToggleHttps.IsEnabled = false;
        BtnToggleHttps.Content = "⏳ Cloudflare HTTPS Tüneli Açılıyor (5-10 sn)...";

        var (success, result) = await _service.StartCloudflareTunnelAsync();
        BtnToggleHttps.IsEnabled = true;

        if (success)
        {
            RefreshServerInfoUI();
            MessageBox.Show($"Resmi Cloudflare HTTPS tüneli açıldı!\n\nLink: {result}\n\niPhone'unuzla soldaki QR kodu okuttuğunuzda Safari canlı kamera iznini doğrudan onaylayacaktır.", "HTTPS Aktif", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            RefreshServerInfoUI();
            MessageBox.Show($"HTTPS Tüneli başlatılamadı:\n{result}\n\nİnternet bağlantınızı kontrol edin veya 'Fotoğrafla Tara' özelliğini kullanabilirsiniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_service.ActiveUrl);
            MessageBox.Show("Bağlantı adresi panoya kopyalandı!\n\nTelefonunuzun tarayıcısına bu adresi yapıştırabilirsiniz.", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }

    private async void DisconnectDevice_Click(object sender, RoutedEventArgs e)
    {
        var device = _service.ActiveDevice;
        if (device != null)
        {
            await _service.DisconnectDeviceAsync(device.Id);
            UpdateDeviceStatusUI();
        }
    }

    private async void RestartServer_Click(object sender, RoutedEventArgs e)
    {
        await _service.StopServerAsync();
        await _service.StartServerAsync();
        RefreshServerInfoUI();
        MessageBox.Show($"Sunucu yeniden başlatıldı.\nYeni Adres: {_service.ActiveUrl}", "Yeniden Başlatıldı", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenFirewallRule_Click(object sender, RoutedEventArgs e)
    {
        var msg = $"Windows Güvenlik Duvarı'nda {_service.Port} portu için gelen bağlantı kuralı oluşturulacaktır.\n\n" +
                  $"Bu işlem Windows yönetici onayı gerektirebilir. Devam etmek istiyor musunuz?";

        if (MessageBox.Show(msg, "Güvenlik Duvarı İzni", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"ERP_PhoneScanner_{_service.Port}\" dir=in action=allow protocol=TCP localport={_service.Port}",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                MessageBox.Show("Güvenlik duvarı kuralı ekleme komutu gönderildi. Telefonunuzdan sayfayı yenileyebilirsiniz.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kural eklenemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
