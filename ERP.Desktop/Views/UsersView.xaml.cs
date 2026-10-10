using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class UsersView : Page
{
    private readonly UserService _userService;
    private readonly ObservableCollection<AppUser> _allUsers = new();
    private readonly ObservableCollection<AppUser> _filteredUsers = new();
    private AppUser? _selectedUser;
    private bool _isLoaded;

    public UsersView()
    {
        InitializeComponent();
        _userService = new UserService();
        LstUsers.ItemsSource = _filteredUsers;

        Loaded += UsersView_Loaded;
    }

    private async void UsersView_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var users = await _userService.GetUsersAsync();
            _allUsers.Clear();
            foreach (var u in users)
            {
                _allUsers.Add(u);
            }

            ApplyFilter();

            if (_selectedUser != null)
            {
                var current = _allUsers.FirstOrDefault(u => u.Id == _selectedUser.Id);
                if (current != null)
                {
                    LstUsers.SelectedItem = current;
                    return;
                }
            }

            if (_filteredUsers.Count > 0)
            {
                LstUsers.SelectedIndex = 0;
            }
            else
            {
                ResetFormForNewUser();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kullanıcılar yüklenirken hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        try
        {
            var query = TxtSearch?.Text?.Trim()?.ToLowerInvariant() ?? "";
            _filteredUsers.Clear();

            var matches = string.IsNullOrWhiteSpace(query)
                ? _allUsers
                : _allUsers.Where(u => (u.FullName?.ToLowerInvariant()?.Contains(query) ?? false) ||
                                      (u.Username?.ToLowerInvariant()?.Contains(query) ?? false) ||
                                      (u.Role?.ToLowerInvariant()?.Contains(query) ?? false) ||
                                      (u.PinCode?.Contains(query) ?? false));

            foreach (var u in matches)
            {
                _filteredUsers.Add(u);
            }
        }
        catch { }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded) return;
        ApplyFilter();
    }

    private void LstUsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;

        try
        {
            if (LstUsers.SelectedItem is AppUser user)
            {
                _selectedUser = user;
                PopulateForm(user);
            }
        }
        catch { }
    }

    private void PopulateForm(AppUser user)
    {
        if (TxtFormTitle == null || TxtFullName == null || TxtUsername == null || 
            TxtPinCode == null || ChkIsActive == null || CmbRole == null || ChkSales == null)
        {
            return;
        }

        try
        {
            TxtFormTitle.Text = $"Kullanıcı Düzenleme: {user.FullName}";
            TxtFullName.Text = user.FullName ?? "";
            TxtUsername.Text = user.Username ?? "";
            TxtPinCode.Text = user.PinCode ?? "";
            ChkIsActive.IsChecked = user.IsActive;

            // Rol Combo
            bool roleFound = false;
            foreach (var item in CmbRole.Items)
            {
                if (item is ComboBoxItem cbi && 
                    cbi.Content?.ToString()?.Equals(user.Role, StringComparison.OrdinalIgnoreCase) == true)
                {
                    CmbRole.SelectedItem = cbi;
                    roleFound = true;
                    break;
                }
            }
            if (!roleFound && CmbRole.Items.Count > 0)
            {
                CmbRole.SelectedIndex = 0;
            }

            // Modül İzinleri
            ChkSales.IsChecked = user.CanAccessSales;
            ChkProducts.IsChecked = user.CanAccessProducts;
            ChkWarehouse.IsChecked = user.CanAccessWarehouse;
            ChkCari.IsChecked = user.CanAccessCari;
            ChkCash.IsChecked = user.CanAccessCash;
            ChkBank.IsChecked = user.CanAccessBank;
            ChkDebts.IsChecked = user.CanAccessDebts;
            ChkInvoices.IsChecked = user.CanAccessInvoices;
            ChkReports.IsChecked = user.CanAccessReports;
            ChkDashboard.IsChecked = user.CanAccessDashboard;
            ChkUsers.IsChecked = user.CanAccessUsers;

            // Özel İşlemler
            ChkClosure.IsChecked = user.CanTakeClosure;
            ChkCancelReceipt.IsChecked = user.CanCancelReceipt;
            ChkManagePrices.IsChecked = user.CanManagePrices;
            ChkApplyDiscount.IsChecked = user.CanApplyDiscount;
            ChkManageDebts.IsChecked = user.CanManageDebts;
            ChkAddCashExpense.IsChecked = user.CanAddCashExpense;
            ChkDepositToBank.IsChecked = user.CanDepositToBank;
            ChkDeleteRecords.IsChecked = user.CanDeleteRecords;

            // 1 numaralı admin silinemez
            if (BtnDeleteUser != null)
            {
                BtnDeleteUser.Visibility = user.Id == 1 ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        catch { }
    }

    private void ResetFormForNewUser()
    {
        if (TxtFormTitle == null || TxtFullName == null) return;

        try
        {
            _selectedUser = null;
            if (LstUsers != null) LstUsers.SelectedItem = null;

            TxtFormTitle.Text = "➕ Yeni Kullanıcı Tanımla";
            TxtFullName.Text = "";
            TxtUsername.Text = "";
            TxtPinCode.Text = "";
            ChkIsActive.IsChecked = true;
            if (CmbRole != null && CmbRole.Items.Count > 0) CmbRole.SelectedIndex = 0; // Kasiyer

            ApplyKasiyerTemplate();

            if (BtnDeleteUser != null) BtnDeleteUser.Visibility = Visibility.Collapsed;
            TxtFullName.Focus();
        }
        catch { }
    }

    private void NewUser_Click(object sender, RoutedEventArgs e)
    {
        ResetFormForNewUser();
    }

    private void ClearForm_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser != null)
        {
            PopulateForm(_selectedUser);
        }
        else
        {
            ResetFormForNewUser();
        }
    }

    private void TemplateAdmin_Click(object sender, RoutedEventArgs e)
    {
        SetAllPermissions(true);
        SelectRoleInCombo("Admin");
    }

    private void TemplateKasiyer_Click(object sender, RoutedEventArgs e)
    {
        ApplyKasiyerTemplate();
        SelectRoleInCombo("Kasiyer");
    }

    private void TemplateNone_Click(object sender, RoutedEventArgs e)
    {
        SetAllPermissions(false);
    }

    private void SetAllPermissions(bool value)
    {
        if (ChkSales == null) return;

        ChkSales.IsChecked = value;
        ChkProducts.IsChecked = value;
        ChkWarehouse.IsChecked = value;
        ChkCari.IsChecked = value;
        ChkCash.IsChecked = value;
        ChkBank.IsChecked = value;
        ChkDebts.IsChecked = value;
        ChkInvoices.IsChecked = value;
        ChkReports.IsChecked = value;
        ChkDashboard.IsChecked = value;
        ChkUsers.IsChecked = value;

        ChkClosure.IsChecked = value;
        ChkCancelReceipt.IsChecked = value;
        ChkManagePrices.IsChecked = value;
        ChkApplyDiscount.IsChecked = value;
        ChkManageDebts.IsChecked = value;
        ChkAddCashExpense.IsChecked = value;
        ChkDepositToBank.IsChecked = value;
        ChkDeleteRecords.IsChecked = value;
    }

    private void ApplyKasiyerTemplate()
    {
        if (ChkSales == null) return;

        ChkSales.IsChecked = true;
        ChkProducts.IsChecked = true;
        ChkWarehouse.IsChecked = false;
        ChkCari.IsChecked = true;
        ChkCash.IsChecked = true;
        ChkBank.IsChecked = false;
        ChkDebts.IsChecked = false;
        ChkInvoices.IsChecked = false;
        ChkReports.IsChecked = true;
        ChkDashboard.IsChecked = false;
        ChkUsers.IsChecked = false;

        ChkClosure.IsChecked = true;
        ChkCancelReceipt.IsChecked = true;
        ChkManagePrices.IsChecked = false;
        ChkApplyDiscount.IsChecked = true;
        ChkManageDebts.IsChecked = false;
        ChkAddCashExpense.IsChecked = true;
        ChkDepositToBank.IsChecked = true;
        ChkDeleteRecords.IsChecked = false;
    }

    private void SelectRoleInCombo(string roleName)
    {
        if (CmbRole == null) return;
        foreach (var item in CmbRole.Items)
        {
            if (item is ComboBoxItem cbi && 
                cbi.Content?.ToString()?.Equals(roleName, StringComparison.OrdinalIgnoreCase) == true)
            {
                CmbRole.SelectedItem = cbi;
                break;
            }
        }
    }

    private async void SaveUser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var fullName = TxtFullName.Text.Trim();
            var username = TxtUsername.Text.Trim();
            var pin = TxtPinCode.Text.Trim();
            var role = (CmbRole.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Kasiyer";

            if (string.IsNullOrWhiteSpace(fullName))
            {
                MessageBox.Show("Lütfen kullanıcının adını ve soyadını giriniz.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtFullName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Lütfen bir kullanıcı adı giriniz.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtUsername.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(pin))
            {
                MessageBox.Show("Lütfen giriş şifresini / PIN kodunu giriniz.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPinCode.Focus();
                return;
            }

            // PIN Çakışması Kontrolü
            var currentId = _selectedUser?.Id ?? 0;
            var existingWithPin = _allUsers.FirstOrDefault(u => u.Id != currentId && u.PinCode == pin);
            if (existingWithPin != null)
            {
                MessageBox.Show($"Bu şifre / PIN ({pin}) zaten '{existingWithPin.FullName}' adlı kullanıcıya aittir!\nLütfen farklı bir şifre belirleyiniz.", 
                                "Şifre Çakışması", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPinCode.SelectAll();
                TxtPinCode.Focus();
                return;
            }

            var user = _selectedUser ?? new AppUser();
            user.FullName = fullName;
            user.Username = username;
            user.PinCode = pin;
            user.Role = role;
            user.IsActive = ChkIsActive.IsChecked ?? true;

            // Modül İzinleri
            user.CanAccessSales = ChkSales.IsChecked ?? false;
            user.CanAccessProducts = ChkProducts.IsChecked ?? false;
            user.CanAccessWarehouse = ChkWarehouse.IsChecked ?? false;
            user.CanAccessCari = ChkCari.IsChecked ?? false;
            user.CanAccessCash = ChkCash.IsChecked ?? false;
            user.CanAccessBank = ChkBank.IsChecked ?? false;
            user.CanAccessDebts = ChkDebts.IsChecked ?? false;
            user.CanAccessInvoices = ChkInvoices.IsChecked ?? false;
            user.CanAccessReports = ChkReports.IsChecked ?? false;
            user.CanAccessDashboard = ChkDashboard.IsChecked ?? false;
            user.CanAccessUsers = ChkUsers.IsChecked ?? false;

            // Özel İşlem İzinleri
            user.CanTakeClosure = ChkClosure.IsChecked ?? false;
            user.CanCancelReceipt = ChkCancelReceipt.IsChecked ?? false;
            user.CanManagePrices = ChkManagePrices.IsChecked ?? false;
            user.CanApplyDiscount = ChkApplyDiscount.IsChecked ?? false;
            user.CanManageDebts = ChkManageDebts.IsChecked ?? false;
            user.CanAddCashExpense = ChkAddCashExpense.IsChecked ?? false;
            user.CanDepositToBank = ChkDepositToBank.IsChecked ?? false;
            user.CanDeleteRecords = ChkDeleteRecords.IsChecked ?? false;

            bool success = await _userService.SaveUserAsync(user);
            if (success)
            {
                _selectedUser = user;
                MessageBox.Show($"'{user.FullName}' kullanıcısı ve yetkileri başarıyla kaydedildi.", "Kayıt Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadUsersAsync();

                if (AuthService.CurrentUser?.Id == user.Id)
                {
                    AuthService.LoginAs(user);
                }
            }
            else
            {
                MessageBox.Show("Kullanıcı kaydedilirken bir hata oluştu.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kayıt sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteUser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_selectedUser == null) return;

            if (_selectedUser.Id == 1)
            {
                MessageBox.Show("Ana Yönetici (Admin) hesabı sistem güvenliği için silinemez!", "Engellendi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var ask = MessageBox.Show($"'{_selectedUser.FullName}' adlı kullanıcıyı silmek istediğinize emin misiniz?\nBu kullanıcı artık sisteme giriş yapamayacaktır.", 
                                      "Kullanıcı Silme Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (ask == MessageBoxResult.Yes)
            {
                bool success = await _userService.DeleteUserAsync(_selectedUser.Id);
                if (success)
                {
                    MessageBox.Show("Kullanıcı başarıyla silindi.", "Silindi", MessageBoxButton.OK, MessageBoxImage.Information);
                    ResetFormForNewUser();
                    await LoadUsersAsync();
                }
                else
                {
                    MessageBox.Show("Kullanıcı silinirken bir hata oluştu.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Silme sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
