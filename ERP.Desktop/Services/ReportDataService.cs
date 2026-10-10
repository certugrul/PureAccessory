using System.Globalization;
using ERP.Desktop.Models;

namespace ERP.Desktop.Services;

public class ReportDataService
{
    private readonly SalesService _salesService;
    private readonly ClosureService _closureService;
    private readonly CashService _cashService;
    private readonly CultureInfo _turkishCulture = new("tr-TR");

    public ReportDataService(SalesService? salesService = null, ClosureService? closureService = null, CashService? cashService = null)
    {
        _salesService = salesService ?? new SalesService();
        _closureService = closureService ?? new ClosureService();
        _cashService = cashService ?? new CashService();
    }

    public async Task<ZReportData?> GetZReportDataAsync(DateTime? targetDate = null)
    {
        var date = (targetDate ?? DateTime.Today).Date;
        var closures = await _closureService.GetClosuresAsync();
        var closure = closures
            .Where(c => c.ClosureDate.Date == date)
            .OrderByDescending(c => c.ClosedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefault();

        if (closure == null && !targetDate.HasValue)
        {
            closure = closures.OrderByDescending(c => c.ClosedAt).ThenByDescending(c => c.Id).FirstOrDefault();
        }

        if (closure == null) return null;

        var cashSession = await _cashService.GetLatestSessionAsync(closure.ClosureDate);

        return new ZReportData
        {
            ClosureId = closure.Id,
            ClosureDate = closure.ClosureDate,
            ClosedAt = closure.ClosedAt,
            SalesCount = closure.SalesCount,
            TotalRevenue = closure.TotalRevenue,
            TotalCash = closure.TotalCash,
            TotalCreditCard = closure.TotalCreditCard,
            TotalOnAccount = closure.TotalOnAccount,
            TotalCost = closure.TotalCost,
            TotalExpenses = closure.TotalExpenses,
            TotalProfit = closure.TotalProfit,
            OpeningCash = cashSession?.OpeningCash ?? 0m,
            ClosingCashBalance = cashSession?.OpeningCash ?? 0m,
            Notes = closure.Notes
        };
    }

    public async Task<XReportData> GetXReportDataAsync(DateTime? targetDate = null)
    {
        var date = (targetDate ?? DateTime.Today).Date;
        var closures = await _closureService.GetClosuresAsync();
        var lastClosure = closures
            .Where(c => c.ClosureDate.Date == date)
            .OrderByDescending(c => c.ClosedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefault();

        var session = await _cashService.GetTodaySessionAsync(date);
        var cashSummary = await _cashService.GetCashSummaryAsync(date);

        // Gün sonu yapılmışsa:
        if (lastClosure != null)
        {
            var allSales = await _salesService.GetSalesForDateAsync(date);
            // Son kapanıştan sonra yapılan satışlar
            var pendingSales = allSales.Where(s => s.SaleDate > lastClosure.ClosedAt).ToList();

            // Eğer kapanıştan sonra yeni satış yoksa -> X raporu 0 görünür
            if (pendingSales.Count == 0)
            {
                return new XReportData
                {
                    ReportTime = DateTime.Now,
                    TargetDate = date,
                    IsCashRegisterOpen = false,
                    CashOpenedAt = null,
                    OpeningCash = 0m,
                    SalesCount = 0,
                    TotalRevenue = 0m,
                    CashSales = 0m,
                    CreditCardSales = 0m,
                    OnAccountSales = 0m,
                    ManualCashIn = 0m,
                    ManualCashOut = 0m,
                    CurrentCashBalance = 0m,
                    TotalCost = 0m,
                    EstimatedProfit = 0m
                };
            }
            else
            {
                // Kapanıştan sonra yeni fişler kesilmişse sadece onları göster
                return new XReportData
                {
                    ReportTime = DateTime.Now,
                    TargetDate = date,
                    IsCashRegisterOpen = cashSummary.IsOpen,
                    CashOpenedAt = session?.OpenedAt,
                    OpeningCash = cashSummary.IsOpen ? cashSummary.OpeningCash : 0m,
                    SalesCount = pendingSales.Count,
                    TotalRevenue = pendingSales.Sum(s => s.GrandTotal),
                    CashSales = pendingSales.Sum(s => s.GetEffectiveCashAmount()),
                    CreditCardSales = pendingSales.Sum(s => s.GetEffectiveCardAmount()),
                    OnAccountSales = pendingSales.Sum(s => s.GetEffectiveOnAccountAmount()),
                    ManualCashIn = 0m,
                    ManualCashOut = 0m,
                    CurrentCashBalance = cashSummary.IsOpen ? cashSummary.CurrentBalance : 0m,
                    TotalCost = pendingSales.Sum(s => s.CostTotal),
                    EstimatedProfit = pendingSales.Sum(s => s.Profit)
                };
            }
        }

        // Gün sonu henüz alınmamış normal durum
        var sales = await _salesService.GetSalesForDateAsync(date);
        if (session != null)
        {
            sales = sales.Where(s => s.SaleDate >= session.OpenedAt).ToList();
        }

        return new XReportData
        {
            ReportTime = DateTime.Now,
            TargetDate = date,
            IsCashRegisterOpen = cashSummary.IsOpen,
            CashOpenedAt = session?.OpenedAt,
            OpeningCash = cashSummary.OpeningCash,
            SalesCount = sales.Count,
            TotalRevenue = sales.Sum(s => s.GrandTotal),
            CashSales = sales.Sum(s => s.GetEffectiveCashAmount()),
            CreditCardSales = sales.Sum(s => s.GetEffectiveCardAmount()),
            OnAccountSales = sales.Sum(s => s.GetEffectiveOnAccountAmount()),
            ManualCashIn = cashSummary.CashIn,
            ManualCashOut = cashSummary.CashOut,
            CurrentCashBalance = cashSummary.CurrentBalance,
            TotalCost = sales.Sum(s => s.CostTotal),
            EstimatedProfit = sales.Sum(s => s.Profit) - cashSummary.CashOut
        };
    }

    public async Task<SalesReportData> GetSalesReportDataAsync(DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date.AddDays(1).AddTicks(-1);

        var allSales = await _salesService.GetAllSalesAsync();
        var filteredSales = allSales
            .Where(s => s.SaleDate >= start && s.SaleDate <= end)
            .OrderByDescending(s => s.SaleDate)
            .ToList();

        var report = new SalesReportData
        {
            StartDate = startDate.Date,
            EndDate = endDate.Date,
            Items = filteredSales.Select(s => new SalesReportItem
            {
                SaleId = s.Id,
                SaleDate = s.SaleDate,
                PaymentType = s.FormattedPaymentDetails,
                ItemCount = s.Items.Count,
                SubTotal = s.SubTotal,
                VatTotal = s.VatTotal,
                GrandTotal = s.GrandTotal,
                CostTotal = s.CostTotal
            }).ToList()
        };

        return report;
    }

    public async Task<MonthlySalesReportData> GetMonthlySalesReportDataAsync(int year, int month)
    {
        var allSales = await _salesService.GetAllSalesAsync();
        var monthSales = allSales.Where(s => s.SaleDate.Year == year && s.SaleDate.Month == month).ToList();

        int daysInMonth = DateTime.DaysInMonth(year, month);
        var daysList = new List<DailyRevenueRow>();
        decimal runningTotal = 0;

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(year, month, day);
            var daySales = monthSales.Where(s => s.SaleDate.Date == date).ToList();

            var cash = daySales.Sum(s => s.GetEffectiveCashAmount());
            var card = daySales.Sum(s => s.GetEffectiveCardAmount());
            var onAccount = daySales.Sum(s => s.GetEffectiveOnAccountAmount());
            var dailyTotal = cash + card + onAccount;
            runningTotal += dailyTotal;

            daysList.Add(new DailyRevenueRow
            {
                Date = date,
                SalesCount = daySales.Count,
                CashTotal = cash,
                CardTotal = card,
                OnAccountTotal = onAccount,
                DailyTotal = dailyTotal,
                CumulativeTotal = runningTotal
            });
        }

        var bestDayRow = daysList.OrderByDescending(d => d.DailyTotal).FirstOrDefault(d => d.DailyTotal > 0);

        return new MonthlySalesReportData
        {
            Year = year,
            Month = month,
            MonthName = new DateTime(year, month, 1).ToString("MMMM yyyy", _turkishCulture),
            Days = daysList,
            BestDay = bestDayRow?.Date,
            BestDayTotal = bestDayRow?.DailyTotal ?? 0m
        };
    }

    public async Task<MonthlyProductReportData> GetMonthlyProductReportDataAsync(int year, int month)
    {
        var allSales = await _salesService.GetAllSalesAsync();
        var monthSales = allSales.Where(s => s.SaleDate.Year == year && s.SaleDate.Month == month).ToList();

        var flatItems = monthSales.SelectMany(s => s.Items).ToList();
        var grouped = flatItems.GroupBy(i => new { i.ProductId, i.ProductCode, i.ProductName }).ToList();

        decimal grandRevenue = flatItems.Sum(i => i.LineTotal);

        var list = new List<ProductSalesPerformanceItem>();
        int rank = 1;

        foreach (var g in grouped.OrderByDescending(x => x.Sum(i => i.LineTotal)))
        {
            var qty = g.Sum(i => i.Quantity);
            var rev = g.Sum(i => i.LineTotal);
            var cost = g.Sum(i => i.Quantity * i.UnitCost);
            var share = grandRevenue > 0 ? (rev / grandRevenue) * 100m : 0m;

            list.Add(new ProductSalesPerformanceItem
            {
                Rank = rank++,
                ProductCode = string.IsNullOrWhiteSpace(g.Key.ProductCode) ? $"PRD-{g.Key.ProductId:D4}" : g.Key.ProductCode,
                ProductName = g.Key.ProductName,
                TotalQuantity = qty,
                TotalRevenue = rev,
                TotalCost = cost,
                RevenueSharePercent = Math.Round(share, 1)
            });
        }

        return new MonthlyProductReportData
        {
            Year = year,
            Month = month,
            MonthName = new DateTime(year, month, 1).ToString("MMMM yyyy", _turkishCulture),
            Products = list
        };
    }

    public async Task<ProfitLossReportData> GetProfitLossReportDataAsync(DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date.AddDays(1).AddTicks(-1);

        var allSales = await _salesService.GetAllSalesAsync();
        var periodSales = allSales.Where(s => s.SaleDate >= start && s.SaleDate <= end).ToList();

        decimal grossRevenue = periodSales.Sum(s => s.GrandTotal);
        decimal vatTotal = periodSales.Sum(s => s.VatTotal);
        decimal costOfGoods = periodSales.Sum(s => s.CostTotal);

        // Kapanışlardaki giderler veya kasa manuel çıkışları
        var closures = await _closureService.GetClosuresAsync();
        var periodClosures = closures.Where(c => c.ClosureDate >= start && c.ClosureDate <= end).ToList();
        decimal operatingExpenses = periodClosures.Sum(c => c.TotalExpenses);

        var ledger = await _cashService.GetDayLedgerAsync();
        var cashOuts = ledger.Where(t => t.CreatedAt >= start && t.CreatedAt <= end && (t.Type == "Nakit Çıkış" || t.Type.Contains("Gider"))).Sum(t => t.Amount);

        // En azından kasa masrafları hesaba katılsın
        if (cashOuts > operatingExpenses) operatingExpenses = cashOuts;

        string periodTitle = startDate.Date == endDate.Date
            ? startDate.ToString("dd MMMM yyyy", _turkishCulture)
            : $"{startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}";

        return new ProfitLossReportData
        {
            StartDate = start,
            EndDate = end,
            PeriodTitle = periodTitle,
            GrossRevenue = grossRevenue,
            VatTotal = vatTotal,
            CostOfGoodsSold = costOfGoods,
            TotalOperatingExpenses = operatingExpenses
        };
    }

    public async Task<YearlySalesReportData> GetYearlyRevenueReportDataAsync(int year)
    {
        var allSales = await _salesService.GetAllSalesAsync();
        var yearSales = allSales.Where(s => s.SaleDate.Year == year).ToList();

        var monthsList = new List<YearlyMonthRow>();
        decimal runningTotal = 0;

        for (int month = 1; month <= 12; month++)
        {
            var mSales = yearSales.Where(s => s.SaleDate.Month == month).ToList();
            var cash = mSales.Sum(s => s.GetEffectiveCashAmount());
            var card = mSales.Sum(s => s.GetEffectiveCardAmount());
            var onAccount = mSales.Sum(s => s.GetEffectiveOnAccountAmount());
            var mTotal = cash + card + onAccount;
            runningTotal += mTotal;

            var mName = new DateTime(year, month, 1).ToString("MMMM", _turkishCulture);

            monthsList.Add(new YearlyMonthRow
            {
                Month = month,
                MonthName = mName,
                SalesCount = mSales.Count,
                CashTotal = cash,
                CardTotal = card,
                OnAccountTotal = onAccount,
                MonthlyTotal = mTotal,
                CumulativeTotal = runningTotal
            });
        }

        var bestMonth = monthsList.OrderByDescending(m => m.MonthlyTotal).FirstOrDefault(m => m.MonthlyTotal > 0);

        return new YearlySalesReportData
        {
            Year = year,
            Months = monthsList,
            BestMonthName = bestMonth?.MonthName ?? "-",
            BestMonthTotal = bestMonth?.MonthlyTotal ?? 0m
        };
    }
}

