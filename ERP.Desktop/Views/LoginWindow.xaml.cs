using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly UserService _userService;
    private List<AppUser> _activeUsers = new();
    private AppUser? _selectedUser;

    public LoginWindow()
    {
        InitializeComponent();
        _userService = new UserService();

        Loaded += LoginWindow_Loaded;
    }

    private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TxtLoginVersion != null)
            {
                TxtLoginVersion.Text = $"v{UpdateService.CurrentVersion}";
            }

            var allUsers = _userService.GetUsers();
            _activeUsers = allUsers.Where(u => u.IsActive).ToList();

            if (_activeUsers.Count == 0)
            {
                _activeUsers = allUsers;
            }

            UsersCardsItemsControl.ItemsSource = _activeUsers;

            // Varsayılan olarak Admin'i veya ilk aktif kullanıcıyı seç
            _selectedUser = _activeUsers.FirstOrDefault(u => u.Id == 1) ?? _activeUsers.FirstOrDefault();
            if (_selectedUser != null)
            {
                SelectUser(_selectedUser);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kullanıcılar yüklenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        TxtPassword.Focus();
        _ = RefreshDbStatusAsync();
    }

    private async Task RefreshDbStatusAsync()
    {
        try
        {
            DotDbStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAB308"));
            TxtDbStatus.Text = "SQL Server: Bağlantı kontrol ediliyor...";

            var dbService = new DatabaseService();
            bool isConnected = await dbService.TestConnectionAsync();

            if (isConnected)
            {
                DotDbStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                TxtDbStatus.Text = "SQL Server: Bağlı (ERPSystem)";
                TxtDbStatus.ToolTip = dbService.ConnectionString;
            }
            else
            {
                DotDbStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                TxtDbStatus.Text = "SQL Server: Bağlantı Yok (Çevrimdışı)";
                TxtDbStatus.ToolTip = "SQL Server servisi durmuş olabilir veya bağlantı geçersizdir.";
            }
        }
        catch { }
    }

    private void ConfigureDb_Click(object sender, RoutedEventArgs e)
    {
        var dbDialog = new DbConnectionDialog { Owner = this };
        if (dbDialog.ShowDialog() == true)
        {
            _ = RefreshDbStatusAsync();
        }
    }

    private void UserCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is AppUser user)
        {
            SelectUser(user);
        }
    }

    private void SelectUser(AppUser user)
    {
        _selectedUser = user;
        TxtSelectedUserLabel.Text = $"Seçili: {user.FullName}";
        TxtPassword.Password = user.PinCode;
        TxtPassword.SelectAll();
        TxtPassword.Focus();

        // Kart kenarlıklarını görsel olarak güncelle
        HighlightSelectedCard(user);
    }

    private void HighlightSelectedCard(AppUser selected)
    {
        try
        {
            for (int i = 0; i < UsersCardsItemsControl.Items.Count; i++)
            {
                var container = UsersCardsItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container != null)
                {
                    var border = FindVisualChild<Border>(container);
                    if (border != null && container.DataContext is AppUser u)
                    {
                        if (u.Id == selected.Id)
                        {
                            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                            border.BorderThickness = new Thickness(2);
                            border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                        }
                        else
                        {
                            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                            border.BorderThickness = new Thickness(1.5);
                            border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DoLogin();
        }
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        DoLogin();
    }

    private async void DoLogin()
    {
        var pin = TxtPassword.Password.Trim();
        if (string.IsNullOrWhiteSpace(pin))
        {
            TxtError.Text = "Lütfen giriş şifrenizi tuşlayınız.";
            TxtError.Visibility = Visibility.Visible;
            TxtPassword.Focus();
            return;
        }

        if (AuthService.LoginWithPin(pin, out var user))
        {
            TxtError.Visibility = Visibility.Collapsed;

            // Giriş başarılı: Uygulamaya girmeden önce tüm operasyonel verileri DB'den önbelleğe al
            BtnLogin.IsEnabled = false;
            BtnExit.IsEnabled = false;
            TxtPassword.IsEnabled = false;
            LoadingPanel.Visibility = Visibility.Visible;

            var progress = new Progress<string>(status =>
            {
                TxtLoadingStatus.Text = status;
            });

            try
            {
                await AppDataCache.PreloadAllAsync(progress);
            }
            catch { }

            DialogResult = true;
        }
        else
        {
            TxtError.Text = "Hatalı şifre veya kullanıcı pasif! Lütfen kontrol ediniz.";
            TxtError.Visibility = Visibility.Visible;
            TxtPassword.SelectAll();
            TxtPassword.Focus();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Application.Current.Shutdown();
        }
        catch
        {
            Environment.Exit(0);
        }
    }
}
