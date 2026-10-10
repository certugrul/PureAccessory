using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class UserService
{
    private readonly DatabaseService _dbService;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string StorageFile = Path.Combine(StorageDirectory, "users.json");
    private static readonly object FileLock = new();
    private static List<AppUser>? _cachedUsers;

    public UserService(DatabaseService? dbService = null)
    {
        _dbService = dbService ?? new DatabaseService();
        EnsureStorageDirectory();
    }

    private static void EnsureStorageDirectory()
    {
        try
        {
            if (!Directory.Exists(StorageDirectory))
            {
                Directory.CreateDirectory(StorageDirectory);
            }
        }
        catch { }
    }

    public List<AppUser> GetUsers()
    {
        lock (FileLock)
        {
            if (_cachedUsers != null && _cachedUsers.Count > 0)
            {
                return _cachedUsers;
            }

            // 1. Önce SQL Server'dan çekmeyi dene
            try
            {
                var sqlUsers = LoadFromSqlServer();
                if (sqlUsers != null && sqlUsers.Count > 0)
                {
                    _cachedUsers = sqlUsers;
                    SaveAllToLocalJson(_cachedUsers);
                    return _cachedUsers;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserService] SQL Server'dan kullanıcılar okunamadı: {ex.Message}");
            }

            // 2. SQL Server yoksa veya boşsa yerel JSON'dan oku
            _cachedUsers = LoadFromLocalJson();
            if (_cachedUsers.Count == 0)
            {
                _cachedUsers = GetDefaultUsers();
                SaveAllToLocalJson(_cachedUsers);
            }
            return _cachedUsers;
        }
    }

    public Task<List<AppUser>> GetUsersAsync()
    {
        return Task.FromResult(GetUsers());
    }

    public AppUser? Authenticate(string pin)
    {
        pin = pin.Trim();
        var users = GetUsers();
        return users.FirstOrDefault(u => u.IsActive && u.PinCode == pin);
    }

    public Task<AppUser?> AuthenticateAsync(string pin)
    {
        return Task.FromResult(Authenticate(pin));
    }

    public async Task<bool> SaveUserAsync(AppUser user)
    {
        return await Task.Run(() =>
        {
            lock (FileLock)
            {
                try
                {
                    var list = GetUsers();

                    if (user.Id == 0)
                    {
                        user.Id = list.Count > 0 ? list.Max(u => u.Id) + 1 : 1;
                        user.CreatedAt = DateTime.Now;
                        list.Add(user);
                    }
                    else
                    {
                        var existingIndex = list.FindIndex(u => u.Id == user.Id);
                        if (existingIndex >= 0)
                        {
                            if (list[existingIndex].Id == 1)
                            {
                                user.IsActive = true;
                                user.CanAccessUsers = true;
                                user.CanAccessDashboard = true;
                            }
                            list[existingIndex] = user;
                        }
                        else
                        {
                            list.Add(user);
                        }
                    }

                    _cachedUsers = list;
                    SaveAllToLocalJson(list);

                    // SQL Server'a kaydet
                    SaveToSqlServer(user);

                    return true;
                }
                catch
                {
                    return false;
                }
            }
        });
    }

    public async Task<bool> DeleteUserAsync(int userId)
    {
        if (userId == 1) return false;

        return await Task.Run(() =>
        {
            lock (FileLock)
            {
                try
                {
                    var list = GetUsers();
                    var removed = list.RemoveAll(u => u.Id == userId) > 0;
                    if (removed)
                    {
                        _cachedUsers = list;
                        SaveAllToLocalJson(list);

                        // SQL Server'dan sil
                        try
                        {
                            using var conn = new SqlConnection(_dbService.ConnectionString);
                            conn.Open();
                            using var cmd = new SqlCommand("DELETE FROM Users WHERE Id = @id", conn);
                            cmd.Parameters.AddWithValue("@id", userId);
                            cmd.ExecuteNonQuery();
                        }
                        catch { }
                    }
                    return removed;
                }
                catch
                {
                    return false;
                }
            }
        });
    }

    public async Task<bool> ChangePasswordAsync(int userId, string newPin)
    {
        if (string.IsNullOrWhiteSpace(newPin)) return false;

        return await Task.Run(() =>
        {
            lock (FileLock)
            {
                try
                {
                    var list = GetUsers();
                    var user = list.FirstOrDefault(u => u.Id == userId);
                    if (user != null)
                    {
                        user.PinCode = newPin.Trim();
                        _cachedUsers = list;
                        SaveAllToLocalJson(list);

                        try
                        {
                            using var conn = new SqlConnection(_dbService.ConnectionString);
                            conn.Open();
                            using var cmd = new SqlCommand("UPDATE Users SET PinCode = @pin WHERE Id = @id", conn);
                            cmd.Parameters.AddWithValue("@pin", user.PinCode);
                            cmd.Parameters.AddWithValue("@id", userId);
                            cmd.ExecuteNonQuery();
                        }
                        catch { }

                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        });
    }

    private List<AppUser>? LoadFromSqlServer()
    {
        using var conn = new SqlConnection(_dbService.ConnectionString);
        conn.Open();

        using var cmd = new SqlCommand("SELECT Id, Username, FullName, Role, PinCode, IsActive, CreatedAt, PermissionsJson FROM Users", conn);
        using var reader = cmd.ExecuteReader();

        var result = new List<AppUser>();
        while (reader.Read())
        {
            int id = reader.GetInt32(0);
            string username = reader.GetString(1);
            string fullName = reader.GetString(2);
            string role = reader.GetString(3);
            string pin = reader.GetString(4);
            bool isActive = reader.GetBoolean(5);
            DateTime createdAt = reader.GetDateTime(6);
            string? permJson = reader.IsDBNull(7) ? null : reader.GetString(7);

            AppUser? user = null;
            if (!string.IsNullOrWhiteSpace(permJson))
            {
                try
                {
                    user = JsonSerializer.Deserialize<AppUser>(permJson);
                }
                catch { }
            }

            if (user == null)
            {
                user = new AppUser
                {
                    Id = id,
                    Username = username,
                    FullName = fullName,
                    Role = role,
                    PinCode = pin,
                    IsActive = isActive,
                    CreatedAt = createdAt,
                    CanAccessSales = true,
                    CanAccessProducts = true,
                    CanAccessWarehouse = role == "Admin",
                    CanAccessCari = true,
                    CanAccessCash = true,
                    CanAccessBank = role == "Admin",
                    CanAccessDebts = role == "Admin",
                    CanAccessInvoices = role == "Admin",
                    CanAccessReports = true,
                    CanAccessDashboard = role == "Admin",
                    CanAccessUsers = role == "Admin",
                    CanTakeClosure = true,
                    CanCancelReceipt = role == "Admin",
                    CanDeleteRecords = role == "Admin",
                    CanManagePrices = role == "Admin",
                    CanApplyDiscount = true,
                    CanManageDebts = role == "Admin",
                    CanAddCashExpense = true,
                    CanDepositToBank = true
                };
            }
            else
            {
                user.Id = id;
                user.Username = username;
                user.FullName = fullName;
                user.Role = role;
                user.PinCode = pin;
                user.IsActive = isActive;
                user.CreatedAt = createdAt;
            }

            result.Add(user);
        }

        return result;
    }

    private void SaveToSqlServer(AppUser user)
    {
        try
        {
            using var conn = new SqlConnection(_dbService.ConnectionString);
            conn.Open();

            var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Id = @id", conn);
            checkCmd.Parameters.AddWithValue("@id", user.Id);
            int exists = Convert.ToInt32(checkCmd.ExecuteScalar());

            string permJson = JsonSerializer.Serialize(user);

            if (exists > 0)
            {
                var updateSql = @"UPDATE Users 
                                  SET Username = @u, FullName = @fn, Role = @role, PinCode = @pin, IsActive = @act, PermissionsJson = @perm 
                                  WHERE Id = @id";
                using var cmd = new SqlCommand(updateSql, conn);
                cmd.Parameters.AddWithValue("@id", user.Id);
                cmd.Parameters.AddWithValue("@u", user.Username);
                cmd.Parameters.AddWithValue("@fn", user.FullName);
                cmd.Parameters.AddWithValue("@role", user.Role);
                cmd.Parameters.AddWithValue("@pin", user.PinCode);
                cmd.Parameters.AddWithValue("@act", user.IsActive);
                cmd.Parameters.AddWithValue("@perm", permJson);
                cmd.ExecuteNonQuery();
            }
            else
            {
                var insertSql = @"SET IDENTITY_INSERT Users ON;
                                  INSERT INTO Users (Id, Username, FullName, Role, PinCode, IsActive, CreatedAt, PermissionsJson)
                                  VALUES (@id, @u, @fn, @role, @pin, @act, @created, @perm);
                                  SET IDENTITY_INSERT Users OFF;";
                using var cmd = new SqlCommand(insertSql, conn);
                cmd.Parameters.AddWithValue("@id", user.Id);
                cmd.Parameters.AddWithValue("@u", user.Username);
                cmd.Parameters.AddWithValue("@fn", user.FullName);
                cmd.Parameters.AddWithValue("@role", user.Role);
                cmd.Parameters.AddWithValue("@pin", user.PinCode);
                cmd.Parameters.AddWithValue("@act", user.IsActive);
                cmd.Parameters.AddWithValue("@created", user.CreatedAt);
                cmd.Parameters.AddWithValue("@perm", permJson);
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserService] SQL Server'a kaydedilemedi: {ex.Message}");
        }
    }

    private static List<AppUser> GetDefaultUsers()
    {
        return new List<AppUser>
        {
            new AppUser
            {
                Id = 1,
                Username = "admin",
                FullName = "Yönetici (Admin)",
                Role = "Admin",
                PinCode = "1",
                IsActive = true,
                CanAccessSales = true,
                CanAccessProducts = true,
                CanAccessWarehouse = true,
                CanAccessCari = true,
                CanAccessCash = true,
                CanAccessBank = true,
                CanAccessInvoices = true,
                CanAccessReports = true,
                CanAccessDashboard = true,
                CanAccessUsers = true,
                CanAccessDebts = true,
                CanTakeClosure = true,
                CanCancelReceipt = true,
                CanDeleteRecords = true,
                CanManagePrices = true,
                CanApplyDiscount = true,
                CanManageDebts = true,
                CanAddCashExpense = true,
                CanDepositToBank = true
            },
            new AppUser
            {
                Id = 2,
                Username = "kasiyer",
                FullName = "Kasiyer",
                Role = "Kasiyer",
                PinCode = "2",
                IsActive = true,
                CanAccessSales = true,
                CanAccessProducts = true,
                CanAccessWarehouse = false,
                CanAccessCari = true,
                CanAccessCash = true,
                CanAccessBank = false,
                CanAccessDebts = false,
                CanAccessInvoices = false,
                CanAccessReports = true,
                CanAccessDashboard = false,
                CanAccessUsers = false,
                CanTakeClosure = true,
                CanCancelReceipt = true,
                CanDeleteRecords = false,
                CanManagePrices = false,
                CanApplyDiscount = true,
                CanManageDebts = false,
                CanAddCashExpense = true,
                CanDepositToBank = true
            }
        };
    }

    private static List<AppUser> LoadFromLocalJson()
    {
        try
        {
            EnsureStorageDirectory();
            if (File.Exists(StorageFile))
            {
                var json = File.ReadAllText(StorageFile);
                var list = JsonSerializer.Deserialize<List<AppUser>>(json);
                if (list != null && list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch { }

        return new List<AppUser>();
    }

    private static void SaveAllToLocalJson(List<AppUser> users)
    {
        try
        {
            EnsureStorageDirectory();
            var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorageFile, json);
        }
        catch { }
    }
}
