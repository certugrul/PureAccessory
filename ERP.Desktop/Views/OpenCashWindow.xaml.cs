using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ERP.Desktop.Models;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class OpenCashWindow : Window
{
    private readonly CashService _cashService;
    public CashSession? OpenedSession { get; private set; }

    public OpenCashWindow()
    {
        InitializeComponent();
        _cashService = new CashService();

        Loaded += async (_, _) =>
        {
            decimal totalVault = await _cashService.GetTotalCashBalanceAsync();
            TxtOpeningCash.Text = NumberParser.FormatForEditing(totalVault);
            TxtOpeningCash.Focus();
            TxtOpeningCash.SelectAll();
        };
    }

    private void PresetCash_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && NumberParser.TryParseDecimal(btn.Tag?.ToString(), out var addVal))
        {
            var current = NumberParser.ParseDecimalSafe(TxtOpeningCash.Text);
            TxtOpeningCash.Text = NumberParser.FormatForEditing(current + addVal);
        }
    }

    private void ResetCash_Click(object sender, RoutedEventArgs e)
    {
        TxtOpeningCash.Text = "0";
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!NumberParser.TryParseDecimal(TxtOpeningCash.Text, out var openingCash))
        {
            MessageBox.Show("Lütfen geçerli bir başlangıç nakit tutarı girin.", "Geçersiz Tutar", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtOpeningCash.Focus();
            return;
        }

        if (openingCash < 0)
        {
            MessageBox.Show("Başlangıç nakiti negatif olamaz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var note = string.IsNullOrWhiteSpace(TxtNotes.Text) ? "Gün Açılışı" : TxtNotes.Text.Trim();
            OpenedSession = await _cashService.OpenCashRegisterAsync(openingCash, note);

            MessageBox.Show($"Kasa gün açılışı başarıyla yapıldı!\n\n• Devreden Açılış Nakiti: ₺{openingCash:N2}\n• Açılış Saati: {OpenedSession.FormattedOpenedAt}\n\nSatış terminali kullanıma hazırdır.", "Gün Açılışı Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kasa açılırken bir hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
