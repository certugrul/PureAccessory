using System.Windows;
using System.Windows.Media;
using ERP.Desktop.Services;

namespace ERP.Desktop.Views;

public partial class CashTransactionDialog : Window
{
    public decimal ResultAmount { get; private set; }
    public string ResultDescription { get; private set; } = "";
    private readonly bool _isExpense;

    public CashTransactionDialog(bool isExpense, decimal defaultAmount = 100, string defaultDescription = "")
    {
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _isExpense = isExpense;

        if (_isExpense)
        {
            Title = "Kasadan Masraf / Para Çıkışı";
            TxtIcon.Text = "➖";
            TxtTitle.Text = "Kasadan Masraf / Nakit Çıkışı";
            TxtTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            TxtSubtitle.Text = "Kasadan ödenecek masraf veya nakit çıkış tutarını giriniz";
            TxtAmount.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            BtnConfirm.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            TxtDescription.Text = string.IsNullOrWhiteSpace(defaultDescription) ? "Genel Masraf" : defaultDescription;
        }
        else
        {
            Title = "Kasaya Nakit Ekle";
            TxtIcon.Text = "➕";
            TxtTitle.Text = "Kasaya Nakit Ekle";
            TxtTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
            TxtSubtitle.Text = "Kasaya ilave edilecek nakit tutarını giriniz";
            TxtAmount.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
            BtnConfirm.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
            TxtDescription.Text = string.IsNullOrWhiteSpace(defaultDescription) ? "Nakit Takviye" : defaultDescription;
        }

        TxtAmount.Text = defaultAmount > 0 ? NumberParser.FormatForEditing(defaultAmount) : "";
        Loaded += (_, _) =>
        {
            TxtAmount.Focus();
            TxtAmount.SelectAll();
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!NumberParser.TryParseDecimal(TxtAmount.Text, out var amt) || amt <= 0)
        {
            MessageBox.Show("Lütfen 0'dan büyük geçerli bir nakit tutar giriniz.", "Geçersiz Tutar", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAmount.Focus();
            return;
        }

        var desc = TxtDescription.Text.Trim();
        if (string.IsNullOrWhiteSpace(desc))
        {
            MessageBox.Show("Lütfen bir açıklama giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtDescription.Focus();
            return;
        }

        ResultAmount = amt;
        ResultDescription = desc;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
