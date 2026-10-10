using ERP.Desktop.Models;

namespace ERP.Desktop.Services;

public static class AuthService
{
    private static readonly UserService _userService = new();

    public static AppUser? CurrentUser { get; private set; }

    public static bool IsLoggedIn => CurrentUser != null;
    public static bool IsAdmin => CurrentUser?.IsAdmin ?? false;
    public static bool IsKasiyer => CurrentUser?.IsKasiyer ?? false;
    public static string CurrentUserName => CurrentUser?.FullName ?? "Misafir";
    public static string CurrentRoleName => CurrentUser?.RoleDisplayName ?? "Yetkisiz";

    public static event Action? UserChanged;

    public static bool LoginWithPin(string pin, out AppUser? user)
    {
        pin = pin.Trim();
        user = _userService.Authenticate(pin);
        if (user != null)
        {
            user.LastLoginAt = DateTime.Now;
            CurrentUser = user;
            _ = _userService.SaveUserAsync(user);
            UserChanged?.Invoke();
            return true;
        }

        return false;
    }

    public static Task<bool> LoginWithPinAsync(string pin)
    {
        return Task.FromResult(LoginWithPin(pin, out _));
    }

    public static void LoginAs(AppUser user)
    {
        user.LastLoginAt = DateTime.Now;
        CurrentUser = user;
        _ = _userService.SaveUserAsync(user);
        UserChanged?.Invoke();
    }

    public static void Logout()
    {
        CurrentUser = null;
        UserChanged?.Invoke();
    }

    public static bool CanAccessModule(string moduleName)
    {
        if (CurrentUser == null) return false;
        if (CurrentUser.IsAdmin) return true;

        return moduleName.ToLower() switch
        {
            "satış" or "sales" => CurrentUser.CanAccessSales,
            "ürünler" or "products" => CurrentUser.CanAccessProducts,
            "depo" or "warehouse" => CurrentUser.CanAccessWarehouse,
            "cari" => CurrentUser.CanAccessCari,
            "kasa" or "kasa gün açılışı" or "cash" => CurrentUser.CanAccessCash,
            "banka" or "bank" => CurrentUser.CanAccessBank,
            "borç" or "finans" or "debts" or "borç & finans" => CurrentUser.CanAccessDebts,
            "faturalar" or "invoices" => CurrentUser.CanAccessInvoices,
            "raporlar" or "reports" => CurrentUser.CanAccessReports,
            "yönetim paneli" or "dashboard" => CurrentUser.CanAccessDashboard,
            "kullanıcı yönetimi" or "users" => CurrentUser.CanAccessUsers,
            _ => false
        };
    }

    public static bool CanTakeClosure => CurrentUser?.CanTakeClosure ?? false;
    public static bool CanCancelReceipt => CurrentUser?.CanCancelReceipt ?? false;
    public static bool CanDeleteRecords => CurrentUser?.CanDeleteRecords ?? false;
    public static bool CanManagePrices => CurrentUser?.CanManagePrices ?? false;
    public static bool CanApplyDiscount => CurrentUser?.CanApplyDiscount ?? false;
    public static bool CanManageDebts => (CurrentUser?.CanManageDebts ?? false) || IsAdmin;
    public static bool CanAddCashExpense => CurrentUser?.CanAddCashExpense ?? false;
    public static bool CanDepositToBank => CurrentUser?.CanDepositToBank ?? false;
}
