using System.Windows;
using ERP.Desktop.Models;

namespace ERP.Desktop.Views;

public partial class InvoiceDetailWindow : Window
{
    public InvoiceDetailWindow(Invoice invoice)
    {
        InitializeComponent();

        TxtInvoiceTitle.Text = $"📄 Fatura: {invoice.InvoiceNo}";
        TxtBadgeTypeText.Text = invoice.Type;

        if (invoice.Type == "Alış Faturası")
        {
            BadgeType.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ECFDF5"));
            BadgeType.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A7F3D0"));
            TxtBadgeTypeText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#059669"));
        }
        else
        {
            BadgeType.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EFF6FF"));
            BadgeType.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#BFDBFE"));
            TxtBadgeTypeText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2563EB"));
        }

        TxtPartyName.Text = invoice.SupplierOrCustomer;
        TxtDate.Text = invoice.FormattedDate;
        TxtWarehouse.Text = invoice.WarehouseName;
        TxtPaymentInfo.Text = invoice.PaymentSummaryText;

        ItemsGrid.ItemsSource = invoice.Items;

        TxtSubTotal.Text = invoice.FormattedSubTotal;
        TxtVatTotal.Text = invoice.FormattedVatTotal;
        TxtGrandTotal.Text = invoice.FormattedGrandTotal;
        TxtNotes.Text = string.IsNullOrWhiteSpace(invoice.Notes) ? "Not: -" : $"Not: {invoice.Notes}";
    }
}
