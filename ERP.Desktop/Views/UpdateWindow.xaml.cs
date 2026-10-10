using System.Windows;
using System.Windows.Input;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _update;
    private bool _isDownloading = false;

    public UpdateWindow(UpdateInfo update)
    {
        InitializeComponent();
        _update = update;

        TxtCurrentVer.Text = $"v{UpdateService.CurrentVersion}";
        TxtRemoteVer.Text = $"v{_update.Version}";
        TxtReleaseDate.Text = !string.IsNullOrWhiteSpace(_update.ReleaseDate) ? $"Yayın: {_update.ReleaseDate}" : "";
        TxtChangeLog.Text = !string.IsNullOrWhiteSpace(_update.ChangeLog) ? _update.ChangeLog : "• Sistem ve performans güncellemeleri.";

        if (_update.Mandatory)
        {
            BtnLater.Visibility = Visibility.Collapsed;
            BtnCloseTop.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !_isDownloading)
        {
            DragMove();
        }
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading) return;

        _isDownloading = true;
        BtnUpdateNow.IsEnabled = false;
        BtnLater.IsEnabled = false;
        BtnCloseTop.IsEnabled = false;
        PanelProgress.Visibility = Visibility.Visible;

        var progress = new Progress<int>(pct =>
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBarDownload.Value = pct;
                TxtProgressPct.Text = $"%{pct}";
            });
        });

        try
        {
            TxtProgressStatus.Text = "Güncelleme paketi indiriliyor...";
            await UpdateService.DownloadAndApplyUpdateAsync(_update, progress);
        }
        catch (Exception ex)
        {
            _isDownloading = false;
            BtnUpdateNow.IsEnabled = true;
            BtnLater.IsEnabled = true;
            BtnCloseTop.IsEnabled = true;
            PanelProgress.Visibility = Visibility.Collapsed;

            MessageBox.Show($"Güncelleme indirilemedi:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading) return;
        DialogResult = false;
        Close();
    }
}
