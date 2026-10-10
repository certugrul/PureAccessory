using System.Data.Common;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class InterestRatesWindow : Window
{
    private readonly DatabaseService _dbService;

    public InterestRatesWindow()
    {
        InitializeComponent();
        _dbService = new DatabaseService();

        Loaded += InterestRatesWindow_Loaded;
    }

    private async void InterestRatesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DpEffectiveDate.SelectedDate = DateTime.Today;
        await LoadRatesAsync();
    }

    private async Task LoadRatesAsync()
    {
        try
        {
            var list = new List<InterestRate>();
            await using var conn = _dbService.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT Id, BankAccountId, AccountType, InterestType, Rate, EffectiveDate, EndDate, BsmvRate, KkdfRate, IsActive, CreatedAt
                FROM InterestRates
                WHERE IsActive = 1
                ORDER BY AccountType ASC, InterestType ASC, EffectiveDate DESC";

            await using var cmd = conn.CreateCommand(sql);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new InterestRate
                {
                    Id = reader.GetInt32(0),
                    BankAccountId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    AccountType = reader.GetString(2),
                    InterestType = reader.GetString(3),
                    Rate = reader.GetDecimal(4),
                    EffectiveDate = reader.GetDateTime(5),
                    EndDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    BsmvRate = reader.GetDecimal(7),
                    KkdfRate = reader.GetDecimal(8),
                    IsActive = reader.GetBoolean(9),
                    CreatedAt = reader.GetDateTime(10)
                });
            }

            DgRates.ItemsSource = list;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Faiz oranları yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddNewRate_Click(object sender, RoutedEventArgs e)
    {
        string accountType = (CmbNewAccountType.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Kredi Kartı";
        string interestType = (CmbNewInterestType.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Akdi Faiz";

        if (!decimal.TryParse(TxtNewRate.Text?.Trim(), out decimal rate) || rate <= 0)
        {
            MessageBox.Show("Lütfen geçerli bir faiz oranı girin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DateTime effectiveDate = DpEffectiveDate.SelectedDate ?? DateTime.Today;

        try
        {
            await using var conn = _dbService.CreateConnection();
            await conn.OpenAsync();
            await using var tran = await conn.BeginTransactionAsync();

            // Geçmiş oranı sonlandır (eğer varsa)
            var endOldSql = @"
                UPDATE InterestRates 
                SET EndDate = @endDate 
                WHERE AccountType = @atype AND InterestType = @itype AND (EndDate IS NULL OR EndDate > @endDate)";

            await using (var endCmd = conn.CreateCommand(endOldSql, tran))
            {
                endCmd.AddParam("endDate", effectiveDate.AddDays(-1));
                endCmd.AddParam("atype", accountType);
                endCmd.AddParam("itype", interestType);
                await endCmd.ExecuteNonQueryAsync();
            }

            // Yeni oranı ekle
            var insSql = @"
                INSERT INTO InterestRates (AccountType, InterestType, Rate, EffectiveDate, BsmvRate, KkdfRate, IsActive, CreatedAt)
                VALUES (@atype, @itype, @rate, @effDate, 5.00, 15.00, 1, GETDATE());";

            await using (var insCmd = conn.CreateCommand(insSql, tran))
            {
                insCmd.AddParam("atype", accountType);
                insCmd.AddParam("itype", interestType);
                insCmd.AddParam("rate", rate);
                insCmd.AddParam("effDate", effectiveDate);
                await insCmd.ExecuteNonQueryAsync();
            }

            await tran.CommitAsync();

            MessageBox.Show("Faiz oranı başarıyla kaydedildi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadRatesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
