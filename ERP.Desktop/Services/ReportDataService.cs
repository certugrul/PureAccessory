using System.Globalization;
using ERP.Desktop.Models;

namespace ERP.Desktop.Services;

public class ReportDataService
{
    private readonly SalesService _salesService;
    private readonly ClosureService _closureService;
    private readonly CashService _cashService;
    private readonly BankService _bankService;
    private readonly CultureInfo _turkishCulture = new("tr-TR");

    public ReportDataService(SalesService? salesService = null, ClosureService? closureService = null, CashService? cashService = null, BankService? bankService = null)
    {
        _salesService = salesService ?? new SalesService();
        _closureService = closureService ?? new ClosureService();
        _cashService = cashService ?? new CashService();
        _bankService = bankService ?? new BankService();
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
        var cashSummary = await _cashService.GetCashSummaryAsync(closure.ClosureDate);
        var sales = await _salesService.GetSalesForDateAsync(closure.ClosureDate);

        var settings = ThemeService.Current;
        var storeName = settings?.StoreName ?? "Pure Accessory";
        var storeSubtitle = settings?.StoreSubtitle ?? "Satış & Finans Yönetim Sistemi";

        // KDV Dağılımı ve İndirimler
        var vatMap = new Dictionary<decimal, (decimal taxable, decimal vat)>();
        decimal totalDiscount = 0;

        foreach (var sale in sales)
        {
            if (sale.Items != null)
            {
                foreach (var item in sale.Items)
                {
                    decimal rate = item.VatRate;
                    decimal lineGross = item.Quantity * item.UnitPrice;
                    decimal lineTaxable = lineGross / (1 + (rate / 100m));
                    decimal lineVat = lineGross - lineTaxable;

                    if (!vatMap.ContainsKey(rate))
                        vatMap[rate] = (0, 0);

                    var current = vatMap[rate];
                    vatMap[rate] = (current.taxable + lineTaxable, current.vat + lineVat);
                }
            }
        }

        var vatBreakdown = vatMap.Select(kv => new VatRateSummary
        {
            Rate = kv.Key,
            TaxableAmount = Math.Round(kv.Value.taxable, 2),
            VatAmount = Math.Round(kv.Value.vat, 2)
        }).OrderBy(x => x.Rate).ToList();

        // Banka hesapları ve POS alacakları
        var bankAccounts = new List<BankAccountBalanceSummary>();
        decimal pendingPos = 0;
        decimal settledPos = 0;

        try
        {
            var accounts = await _bankService.GetAllAccountsAsync();
            foreach (var acc in accounts)
            {
                bankAccounts.Add(new BankAccountBalanceSummary
                {
                    BankName = acc.BankName,
                    AccountName = acc.AccountName,
                    Balance = acc.CurrentBalance
                });
            }

            var bankSummary = await _bankService.GetBankSummaryAsync();
            pendingPos = bankSummary.PendingGrossPos;
            settledPos = bankSummary.TodayPosInflow;
        }
        catch { }

        decimal openingCash = cashSession?.OpeningCash ?? cashSummary.OpeningCash;
        decimal closingCash = cashSummary.CurrentBalance;

        return new ZReportData
        {
            ClosureId = closure.Id,
            ReportNumber = $"Z-{closure.ClosureDate:yyyyMMdd}-{closure.Id:D4}",
            StoreName = storeName,
            StoreSubtitle = storeSubtitle,
            ClosureDate = closure.ClosureDate,
            ClosedAt = closure.ClosedAt,
            SalesCount = closure.SalesCount,
            TotalRevenue = closure.TotalRevenue,
            TotalCash = closure.TotalCash,
            TotalCreditCard = closure.TotalCreditCard,
            TotalOnAccount = closure.TotalOnAccount,
            TotalDiscount = totalDiscount,
            TotalCost = closure.TotalCost,
            TotalExpenses = closure.TotalExpenses,
            TotalProfit = closure.TotalProfit,
            PendingPosAmount = pendingPos,
            SettledPosAmount = settledPos,
            BankAccounts = bankAccounts,
            VatBreakdown = vatBreakdown,
            TotalVatAmount = vatBreakdown.Sum(v => v.VatAmount),
            OpeningCash = openingCash,
            CashInflows = cashSummary.CashIn,
            CashOutflows = cashSummary.CashOut,
            ClosingCashBalance = closingCash,
            ZReportCash = closure.ZReportCash,
            ZReportCredit = closure.ZReportCredit,
            CashDiff = closure.CashDiff,
            CreditDiff = closure.CreditDiff,
            IbanAmount = closure.IbanAmount,
            ExpectedBankPayment = closure.ExpectedBankPayment,
            PocketCash = closure.PocketCash,
            PocketCredit = closure.PocketCredit,
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
        var end = endDate.Date.AddDays(1);

        var filteredSales = await _salesService.GetSalesBetweenDatesAsync(start, end);

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
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);
        var monthSales = await _salesService.GetSalesBetweenDatesAsync(start, end);

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
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);
        var monthSales = await _salesService.GetSalesBetweenDatesAsync(start, end);

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
        var end = endDate.Date.AddDays(1);

        var periodSales = await _salesService.GetSalesBetweenDatesAsync(start, end);

        decimal grossRevenue = periodSales.Sum(s => s.GrandTotal);
        decimal vatTotal = periodSales.Sum(s => s.VatTotal);
        decimal costOfGoods = periodSales.Sum(s => s.CostTotal);

        // Kapanışlardaki giderler veya kasa manuel çıkışları
        var closures = await _closureService.GetClosuresAsync();
        var periodClosures = closures.Where(c => c.ClosureDate >= start && c.ClosureDate < end).ToList();
        decimal operatingExpenses = periodClosures.Sum(c => c.TotalExpenses);

        var ledger = await _cashService.GetDayLedgerAsync();
        var cashOuts = ledger.Where(t => t.CreatedAt >= start && t.CreatedAt < end && (t.Type == "Nakit Çıkış" || t.Type.Contains("Gider"))).Sum(t => t.Amount);

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
        var start = new DateTime(year, 1, 1);
        var end = start.AddYears(1);
        var yearSales = await _salesService.GetSalesBetweenDatesAsync(start, end);

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

