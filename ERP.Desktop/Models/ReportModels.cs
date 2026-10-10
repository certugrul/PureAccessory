namespace ERP.Desktop.Models;

public class ZReportData
{
    public long ClosureId { get; set; }
    public DateTime ClosureDate { get; set; }
    public DateTime ClosedAt { get; set; }
    public int SalesCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCash { get; set; }
    public decimal TotalCreditCard { get; set; }
    public decimal TotalOnAccount { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ClosingCashBalance { get; set; }
    public string? Notes { get; set; }
}

public class XReportData
{
    public DateTime ReportTime { get; set; } = DateTime.Now;
    public DateTime TargetDate { get; set; } = DateTime.Today;
    public bool IsCashRegisterOpen { get; set; }
    public DateTime? CashOpenedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public int SalesCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal CashSales { get; set; }
    public decimal CreditCardSales { get; set; }
    public decimal OnAccountSales { get; set; }
    public decimal ManualCashIn { get; set; }
    public decimal ManualCashOut { get; set; }
    public decimal CurrentCashBalance { get; set; }
    public decimal TotalCost { get; set; }
    public decimal EstimatedProfit { get; set; }
}

public class SalesReportItem
{
    public long SaleId { get; set; }
    public DateTime SaleDate { get; set; }
    public string PaymentType { get; set; } = "Nakit";
    public int ItemCount { get; set; }
    public decimal SubTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal CostTotal { get; set; }
    public decimal Profit => GrandTotal - CostTotal;
}

public class SalesReportData
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<SalesReportItem> Items { get; set; } = new();
    public int TotalSalesCount => Items.Count;
    public decimal TotalRevenue => Items.Sum(x => x.GrandTotal);
    public decimal TotalCash => Items.Where(x => x.PaymentType == "Nakit").Sum(x => x.GrandTotal);
    public decimal TotalCreditCard => Items.Where(x => x.PaymentType == "Kredi Kartı" || x.PaymentType == "Kart").Sum(x => x.GrandTotal);
    public decimal TotalOnAccount => Items.Where(x => x.PaymentType == "Veresiye").Sum(x => x.GrandTotal);
    public decimal TotalVat => Items.Sum(x => x.VatTotal);
    public decimal TotalCost => Items.Sum(x => x.CostTotal);
    public decimal TotalProfit => Items.Sum(x => x.Profit);
}

public class DailyRevenueRow
{
    public DateTime Date { get; set; }
    public int SalesCount { get; set; }
    public decimal CashTotal { get; set; }
    public decimal CardTotal { get; set; }
    public decimal OnAccountTotal { get; set; }
    public decimal DailyTotal { get; set; }
    public decimal CumulativeTotal { get; set; }
}

public class MonthlySalesReportData
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = "";
    public List<DailyRevenueRow> Days { get; set; } = new();
    public int TotalSalesCount => Days.Sum(x => x.SalesCount);
    public decimal TotalCash => Days.Sum(x => x.CashTotal);
    public decimal TotalCreditCard => Days.Sum(x => x.CardTotal);
    public decimal TotalOnAccount => Days.Sum(x => x.OnAccountTotal);
    public decimal GrandTotal => Days.Sum(x => x.DailyTotal);
    public decimal DailyAverage => Days.Count > 0 ? GrandTotal / Days.Count : 0m;
    public DateTime? BestDay { get; set; }
    public decimal BestDayTotal { get; set; }
}

public class ProductSalesPerformanceItem
{
    public int Rank { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal TotalQuantity { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal Profit => TotalRevenue - TotalCost;
    public decimal AverageUnitPrice => TotalQuantity > 0 ? TotalRevenue / TotalQuantity : 0m;
    public decimal RevenueSharePercent { get; set; }
}

public class MonthlyProductReportData
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = "";
    public List<ProductSalesPerformanceItem> Products { get; set; } = new();
    public decimal TotalQuantitySold => Products.Sum(x => x.TotalQuantity);
    public decimal TotalRevenue => Products.Sum(x => x.TotalRevenue);
    public decimal TotalProfit => Products.Sum(x => x.Profit);
}

public class ProfitLossReportData
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string PeriodTitle { get; set; } = "";
    
    // Gelirler
    public decimal GrossRevenue { get; set; }
    public decimal VatTotal { get; set; }
    public decimal NetSalesRevenue => GrossRevenue - VatTotal;

    // Satılan Malın Maliyeti (STMM)
    public decimal CostOfGoodsSold { get; set; }
    
    // Brüt Faaliyet Kârı
    public decimal GrossProfit => GrossRevenue - CostOfGoodsSold;
    
    // Faaliyet Giderleri (Kasa çıkışları, masraflar)
    public decimal TotalOperatingExpenses { get; set; }

    // Net Kâr / Zarar
    public decimal NetProfitOrLoss => GrossProfit - TotalOperatingExpenses;
    public decimal ProfitMarginPercent => GrossRevenue > 0 ? (NetProfitOrLoss / GrossRevenue) * 100m : 0m;
}

public class YearlyMonthRow
{
    public int Month { get; set; }
    public string MonthName { get; set; } = "";
    public int SalesCount { get; set; }
    public decimal CashTotal { get; set; }
    public decimal CardTotal { get; set; }
    public decimal OnAccountTotal { get; set; }
    public decimal MonthlyTotal { get; set; }
    public decimal CumulativeTotal { get; set; }
}

public class YearlySalesReportData
{
    public int Year { get; set; }
    public List<YearlyMonthRow> Months { get; set; } = new();
    public int TotalSalesCount => Months.Sum(x => x.SalesCount);
    public decimal TotalCash => Months.Sum(x => x.CashTotal);
    public decimal TotalCreditCard => Months.Sum(x => x.CardTotal);
    public decimal TotalOnAccount => Months.Sum(x => x.OnAccountTotal);
    public decimal GrandTotal => Months.Sum(x => x.MonthlyTotal);
    public decimal MonthlyAverage => Months.Count(m => m.MonthlyTotal > 0) > 0 
        ? GrandTotal / Months.Count(m => m.MonthlyTotal > 0) 
        : (GrandTotal / 12m);
    public string BestMonthName { get; set; } = "";
    public decimal BestMonthTotal { get; set; }
}

public class ReportChartItem
{
    public string Label { get; set; } = "";
    public string SubLabel { get; set; } = "";
    public decimal Value { get; set; }
    public string DisplayValue { get; set; } = "";
    public double BarHeight { get; set; }
    public string BarColor { get; set; } = "#3B82F6";
    public string Tooltip { get; set; } = "";
}

public class ReportChartData
{
    public string ChartTitle { get; set; } = "Grafik Analizi";
    public string ToplamLabel { get; set; } = "Toplam";
    public string ToplamText { get; set; } = "₺0,00";
    public string OrtalamaLabel { get; set; } = "Ortalama";
    public string OrtalamaText { get; set; } = "₺0,00";
    public string ExtraLabel { get; set; } = "En Yüksek";
    public string ExtraText { get; set; } = "-";
    public List<ReportChartItem> Items { get; set; } = new();
}

