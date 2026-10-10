using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class HistoricalDataImportService
{
    private readonly DatabaseService _dbService;

    private static readonly Dictionary<string, int> TurkishMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        { "OCAK", 1 }, { "SUBAT", 2 }, { "MART", 3 }, { "NISAN", 4 },
        { "MAYIS", 5 }, { "HAZIRAN", 6 }, { "TEMMUZ", 7 }, { "AGUSTOS", 8 },
        { "EYLUL", 9 }, { "EKIM", 10 }, { "KASIM", 11 }, { "ARALIK", 12 }
    };

    public HistoricalDataImportService(DatabaseService? dbService = null)
    {
        _dbService = dbService ?? new DatabaseService();
    }

    public static string FindExcelFilePath()
    {
        string[] candidates = new[]
        {
            @"c:\Users\krale\Desktop\ERPStarter\GUNSONU_RAPORU.xlsx",
            @"C:\Users\krale\.gemini\antigravity\brain\2503f5fc-86e1-4e3b-b50e-1ac54b22a867\.user_uploaded\media_1791480991620_34b8ea17.xlsx"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException("Excel dosyası bulunamadı.");
    }

    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return s.Replace("İ", "I").Replace("ı", "i")
                .Replace("Ş", "S").Replace("ş", "s")
                .Replace("Ğ", "G").Replace("ğ", "g")
                .Replace("Ü", "U").Replace("ü", "u")
                .Replace("Ö", "O").Replace("ö", "o")
                .Replace("Ç", "C").Replace("ç", "c")
                .Trim().ToUpperInvariant();
    }

    public static (int headerRow, Dictionary<string, int> map) DetectHeaders(IXLWorksheet ws)
    {
        int maxRow = Math.Min(10, ws.LastRowUsed()?.RowNumber() ?? 0);

        for (int r = 1; r <= maxRow; r++)
        {
            var row = ws.Row(r);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            var tempMap = new Dictionary<string, int>();

            for (int c = 1; c <= lastCol; c++)
            {
                string norm = Normalize(row.Cell(c).GetString());
                if (string.IsNullOrEmpty(norm)) continue;

                if (norm.Contains("NAKIT") && norm.Contains("SATIS")) tempMap["NAKIT_SATIS"] = c;
                else if (norm.Contains("KREDI") && norm.Contains("SATIS")) tempMap["KREDI_SATIS"] = c;
                else if (norm.Contains("Z") && norm.Contains("NAKIT")) tempMap["Z_NAKIT"] = c;
                else if (norm.Contains("Z") && norm.Contains("KREDI")) tempMap["Z_KREDI"] = c;
                else if (norm.Contains("YATACAK")) tempMap["YATACAK_OLAN"] = c;
                else if (norm.Contains("NAKIT") && norm.Contains("FARK")) tempMap["NAKIT_FARK"] = c;
                else if (norm.Contains("KREDI") && norm.Contains("FARK")) tempMap["KREDI_FARK"] = c;
                else if (norm.Contains("NAKIT") && norm.Contains("HARC")) tempMap["NAKIT_HARCAMA"] = c;
                else if (norm.Contains("KREDI") && norm.Contains("HARC")) tempMap["KREDI_HARCAMA"] = c;
                else if (norm == "KDV" || norm.StartsWith("KDV")) tempMap["KDV"] = c;
                else if (norm.Contains("CIRO")) tempMap["CIRO"] = c;
                else if (norm.Contains("CEP") && norm.Contains("NAKIT")) tempMap["CEP_NAKIT"] = c;
                else if (norm.Contains("CEP") && norm.Contains("KREDI")) tempMap["CEP_KREDI"] = c;
                else if (norm.Contains("TARIH") || (norm.Contains("SATIS") && !norm.Contains("NAKIT") && !norm.Contains("KREDI"))) tempMap["TARIH"] = c;
            }

            if (tempMap.ContainsKey("NAKIT_SATIS") && tempMap.ContainsKey("KREDI_SATIS"))
            {
                if (!tempMap.ContainsKey("TARIH")) tempMap["TARIH"] = 1;
                return (r, tempMap);
            }
        }

        return (0, new Dictionary<string, int>());
    }

    public static DateTime? TryParseDate(IXLCell cell, string sheetName = "", DateTime? prevDate = null)
    {
        if (cell.IsEmpty()) return null;

        try
        {
            if (cell.DataType == XLDataType.DateTime)
            {
                return cell.GetDateTime().Date;
            }

            if (cell.DataType == XLDataType.Number)
            {
                double d = cell.GetDouble();
                if (d > 30000 && d < 60000)
                {
                    return DateTime.FromOADate(d).Date;
                }
            }

            string s = cell.GetString().Trim();
            if (string.IsNullOrEmpty(s) || s.Equals("TOPLAM", StringComparison.OrdinalIgnoreCase)) return null;

            // Pattern: 24 Mayıs 2026 Cumartesi
            var matchText = Regex.Match(s, @"(\d{1,2})\s+([A-Za-zÇçĞğİıÖöŞşÜü]+)\s+(\d{4})");
            if (matchText.Success)
            {
                int day = int.Parse(matchText.Groups[1].Value);
                string mName = Normalize(matchText.Groups[2].Value);
                int year = int.Parse(matchText.Groups[3].Value);

                if (TurkishMonths.TryGetValue(mName, out int month))
                {
                    string normSheet = Normalize(sheetName);
                    if (normSheet.Contains("AGUSTOS") && year == 2026 && month == 7 && day == 31 && prevDate.HasValue && prevDate.Value.Month == 8)
                    {
                        month = 8; // Ağustos 31
                    }

                    return new DateTime(year, month, day);
                }
            }

            // Pattern: 01.10.2024 veya 1.10.2024
            var matchNum = Regex.Match(s, @"(\d{1,2})[./-](\d{1,2})[./-](\d{4})");
            if (matchNum.Success)
            {
                int day = int.Parse(matchNum.Groups[1].Value);
                int month = int.Parse(matchNum.Groups[2].Value);
                int year = int.Parse(matchNum.Groups[3].Value);
                return new DateTime(year, month, day);
            }

            if (DateTime.TryParse(s, new CultureInfo("tr-TR"), DateTimeStyles.None, out var dtTr))
                return dtTr.Date;

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtInv))
                return dtInv.Date;
        }
        catch { }

        return null;
    }

    public static decimal GetDecimalValue(IXLCell cell)
    {
        if (cell.IsEmpty()) return 0m;
        try
        {
            if (cell.DataType == XLDataType.Number)
            {
                return Convert.ToDecimal(cell.GetDouble());
            }

            string s = cell.GetString().Trim();
            if (string.IsNullOrEmpty(s) || s == "?" || s == "-") return 0m;

            s = s.Replace("₺", "").Replace("TL", "").Trim();
            if (decimal.TryParse(s, NumberStyles.Any, new CultureInfo("tr-TR"), out var dTr))
                return dTr;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dInv))
                return dInv;
        }
        catch { }

        return 0m;
    }

    public class DayImportData
    {
        public DateTime Date { get; set; }
        public string SheetName { get; set; } = "";
        public int RowIndex { get; set; }

        public decimal NakitSatis { get; set; }
        public decimal KrediSatis { get; set; }
        public decimal ZRaporuNakit { get; set; }
        public decimal ZRaporuKredi { get; set; }
        public decimal YatacakOlan { get; set; }
        public decimal NakitFark { get; set; }
        public decimal KrediFark { get; set; }
        public decimal Kdv { get; set; }
        public decimal NakitHarcama { get; set; }
        public decimal KrediHarcama { get; set; }
        public decimal Ciro { get; set; }
        public decimal CepNakit { get; set; }
        public decimal CepKredi { get; set; }

        public decimal CalculatedCiro => NakitSatis + KrediSatis;
        public decimal IbanTutari => KrediSatis > ZRaporuKredi ? (KrediSatis - ZRaporuKredi) : 0m;
        public decimal TotalExpenses => NakitHarcama + KrediHarcama;
    }

    public class ImportResult
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int ImportedDays { get; set; }
        public decimal TotalCashSales { get; set; }
        public decimal TotalCreditSales { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalCashExpenses { get; set; }
        public decimal TotalCreditExpenses { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal TotalIbanClosures { get; set; }
        public decimal TotalZReportCredit { get; set; }
        public decimal TotalZReportCash { get; set; }
        public decimal TotalVat { get; set; }
        public decimal TotalExpectedBankPayment { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }

        public List<(DateTime Date, decimal KrediSatis, decimal ZRaporuKredi, decimal IbanTutari)> IbanClosures { get; } = new();
        public List<(DateTime Date, string Type, string Message)> Warnings { get; } = new();
    }

    public async Task<ImportResult> ExecuteImportAsync()
    {
        var result = new ImportResult();
        var filePath = FindExcelFilePath();
        Console.WriteLine($"[HistoricalImport] Excel dosyası: {filePath}");

        // 1. Tabloların varlığından emin ol
        var syncService = new DatabaseSyncService(_dbService);
        await syncService.EnsureTablesCreatedAsync();

        // 2. DailyClosures view oluştur (geriye dönük uyumluluk)
        await using (var connInit = new SqlConnection(_dbService.ConnectionString))
        {
            await connInit.OpenAsync();
            var viewSql = "CREATE OR ALTER VIEW DailyClosures AS SELECT * FROM Closures;";
            await using var cmdView = new SqlCommand(viewSql, connInit);
            await cmdView.ExecuteNonQueryAsync();

            // BankAccounts tablosunda en az bir banka hesabı var mı kontrol et
            var checkBankSql = "SELECT COUNT(*) FROM BankAccounts";
            await using var cmdCheckBank = new SqlCommand(checkBankSql, connInit);
            int bankCount = Convert.ToInt32(await cmdCheckBank.ExecuteScalarAsync());
            if (bankCount == 0)
            {
                var insertBankSql = @"
                    INSERT INTO BankAccounts (BankName, AccountName, Currency, PosCommissionRate, PosValourDays, IsDefault, IsActive)
                    VALUES ('Ziraat Bankası', 'Ana Ticari Hesap', 'TRY', 3.99, 2, 1, 1);";
                await using var cmdInsertBank = new SqlCommand(insertBankSql, connInit);
                await cmdInsertBank.ExecuteNonQueryAsync();
            }
        }

        // 3. Mevcut finansal verileri temizle (Kullanıcılar ve Ürünler KORUNUR)
        Console.WriteLine("[HistoricalImport] Mevcut banka, gün sonu, satış, fatura verileri temizleniyor...");
        await using (var connClear = new SqlConnection(_dbService.ConnectionString))
        {
            await connClear.OpenAsync();
            var clearSql = @"
                DELETE FROM ImportWarnings;
                DELETE FROM Closures;
                DELETE FROM SaleItems;
                DELETE FROM Sales;
                DBCC CHECKIDENT ('Sales', RESEED, 0);
                DELETE FROM CashTransactions;
                DELETE FROM CashSessions;
                DBCC CHECKIDENT ('CashSessions', RESEED, 0);
                DELETE FROM InvoiceItems;
                DELETE FROM Invoices;
                DBCC CHECKIDENT ('Invoices', RESEED, 0);
                DELETE FROM PosTransactions;
                DELETE FROM BankTransactions;
                UPDATE BankAccounts SET CurrentBalance = 0, ActualBalance = 0, OpeningBalance = 0;
            ";
            await using var cmdClear = new SqlCommand(clearSql, connClear);
            await cmdClear.ExecuteNonQueryAsync();
        }
        Console.WriteLine("[HistoricalImport] Tablolar başarıyla sıfırlandı.");

        // Varsayılan banka hesabını al
        int defaultBankId = 1;
        string defaultBankName = "Ana Ticari Hesap";
        await using (var connBank = new SqlConnection(_dbService.ConnectionString))
        {
            await connBank.OpenAsync();
            var getBankSql = "SELECT TOP 1 Id, BankName + ' - ' + AccountName FROM BankAccounts ORDER BY IsDefault DESC, Id ASC";
            await using var cmdBank = new SqlCommand(getBankSql, connBank);
            await using var readerBank = await cmdBank.ExecuteReaderAsync();
            if (await readerBank.ReadAsync())
            {
                defaultBankId = readerBank.GetInt32(0);
                defaultBankName = readerBank.GetString(1);
            }
        }

        // 4. Excel verilerini topla
        Console.WriteLine("[HistoricalImport] Excel taranıyor ve veriler ayıklanıyor...");
        using var workbook = new XLWorkbook(filePath);
        var minDate = new DateTime(2024, 10, 1);
        var maxDate = new DateTime(2026, 10, 8);

        var daysList = new List<DayImportData>();
        var seenDates = new HashSet<DateTime>();

        foreach (var ws in workbook.Worksheets)
        {
            string sheetName = ws.Name.Trim();
            string normSheet = Normalize(sheetName);

            if (normSheet.Contains("GENEL") || normSheet == "SAYFA1" || normSheet == "2025" || normSheet == "OZET")
            {
                continue;
            }

            var (headerRow, map) = DetectHeaders(ws);
            if (headerRow == 0) continue;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            DateTime? prevDate = null;

            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                var dateCell = ws.Cell(r, map["TARIH"]);
                var dt = TryParseDate(dateCell, sheetName, prevDate);
                if (!dt.HasValue) continue;

                if (dt.Value < minDate || dt.Value > maxDate) continue;

                decimal nakitSatis = map.ContainsKey("NAKIT_SATIS") ? GetDecimalValue(ws.Cell(r, map["NAKIT_SATIS"])) : 0;
                decimal krediSatis = map.ContainsKey("KREDI_SATIS") ? GetDecimalValue(ws.Cell(r, map["KREDI_SATIS"])) : 0;
                decimal zNakit = map.ContainsKey("Z_NAKIT") ? GetDecimalValue(ws.Cell(r, map["Z_NAKIT"])) : 0;
                decimal zKredi = map.ContainsKey("Z_KREDI") ? GetDecimalValue(ws.Cell(r, map["Z_KREDI"])) : 0;
                decimal yatacakOlan = map.ContainsKey("YATACAK_OLAN") ? GetDecimalValue(ws.Cell(r, map["YATACAK_OLAN"])) : 0;
                decimal nakitFark = map.ContainsKey("NAKIT_FARK") ? GetDecimalValue(ws.Cell(r, map["NAKIT_FARK"])) : 0;
                decimal krediFark = map.ContainsKey("KREDI_FARK") ? GetDecimalValue(ws.Cell(r, map["KREDI_FARK"])) : 0;
                decimal kdv = map.ContainsKey("KDV") ? GetDecimalValue(ws.Cell(r, map["KDV"])) : 0;
                decimal nakitHarcama = map.ContainsKey("NAKIT_HARCAMA") ? GetDecimalValue(ws.Cell(r, map["NAKIT_HARCAMA"])) : 0;
                decimal krediHarcama = map.ContainsKey("KREDI_HARCAMA") ? GetDecimalValue(ws.Cell(r, map["KREDI_HARCAMA"])) : 0;
                decimal ciro = map.ContainsKey("CIRO") ? GetDecimalValue(ws.Cell(r, map["CIRO"])) : 0;
                decimal cepNakit = map.ContainsKey("CEP_NAKIT") ? GetDecimalValue(ws.Cell(r, map["CEP_NAKIT"])) : 0;
                decimal cepKredi = map.ContainsKey("CEP_KREDI") ? GetDecimalValue(ws.Cell(r, map["CEP_KREDI"])) : 0;

                // Boş satır kontrolü
                if (nakitSatis == 0 && krediSatis == 0 && ciro == 0 && nakitHarcama == 0 && krediHarcama == 0 && zKredi == 0)
                {
                    continue;
                }

                if (seenDates.Contains(dt.Value))
                {
                    continue;
                }

                seenDates.Add(dt.Value);
                prevDate = dt.Value;

                daysList.Add(new DayImportData
                {
                    Date = dt.Value,
                    SheetName = sheetName,
                    RowIndex = r,
                    NakitSatis = nakitSatis,
                    KrediSatis = krediSatis,
                    ZRaporuNakit = zNakit,
                    ZRaporuKredi = zKredi,
                    YatacakOlan = yatacakOlan,
                    NakitFark = nakitFark,
                    KrediFark = krediFark,
                    Kdv = kdv,
                    NakitHarcama = nakitHarcama,
                    KrediHarcama = krediHarcama,
                    Ciro = ciro,
                    CepNakit = cepNakit,
                    CepKredi = cepKredi
                });
            }
        }

        // Kronolojik sıralama
        daysList = daysList.OrderBy(d => d.Date).ToList();
        Console.WriteLine($"[HistoricalImport] Toplam {daysList.Count} adet gün aktarıma hazır.");

        if (daysList.Count == 0)
        {
            throw new InvalidOperationException("Aktarılacak geçerli gün bulunamadı.");
        }

        result.StartDate = daysList.First().Date;
        result.EndDate = daysList.Last().Date;

        // 5. Veritabanına transaction ile satır satır işleme
        await using var conn = new SqlConnection(_dbService.ConnectionString);
        await conn.OpenAsync();

        foreach (var day in daysList)
        {
            await using var tx = conn.BeginTransaction();
            try
            {
                var dt = day.Date;

                // Ciro kontrolü
                if (day.Ciro > 0 && Math.Abs(day.CalculatedCiro - day.Ciro) > 0.05m)
                {
                    string msg = $"Hesaplanan Ciro={day.CalculatedCiro:N2} TL, Excel Ciro={day.Ciro:N2} TL (Fark={day.CalculatedCiro - day.Ciro:N2} TL)";
                    result.Warnings.Add((dt, "CİRO UYUŞMAZLIĞI", msg));
                    await InsertWarningAsync(conn, tx, dt, day.SheetName, day.RowIndex, "CİRO UYUŞMAZLIĞI", msg);
                }

                // Cep Nakit kontrolü
                decimal expectedPocketCash = day.NakitSatis - day.NakitHarcama;
                if (day.CepNakit > 0 && Math.Abs(expectedPocketCash - day.CepNakit) > 0.05m)
                {
                    string msg = $"Hesaplanan Cep Nakit={expectedPocketCash:N2} TL, Excel Cep Nakit={day.CepNakit:N2} TL (Fark={expectedPocketCash - day.CepNakit:N2} TL)";
                    result.Warnings.Add((dt, "CEP NAKİT UYUŞMAZLIĞI", msg));
                    await InsertWarningAsync(conn, tx, dt, day.SheetName, day.RowIndex, "CEP NAKİT UYUŞMAZLIĞI", msg);
                }

                // Kredi Satış < Z Raporu Kredi kontrolü
                if (day.KrediSatis < day.ZRaporuKredi)
                {
                    string msg = $"Kredi Satış ({day.KrediSatis:N2} TL) < Z Raporu Kredi ({day.ZRaporuKredi:N2} TL), Fark={day.ZRaporuKredi - day.KrediSatis:N2} TL";
                    result.Warnings.Add((dt, "KREDİ FARK", msg));
                    await InsertWarningAsync(conn, tx, dt, day.SheetName, day.RowIndex, "KREDİ FARK", msg);
                }

                // IBAN Kapanışı
                decimal ibanAmount = day.IbanTutari;
                if (ibanAmount > 0)
                {
                    result.IbanClosures.Add((dt, day.KrediSatis, day.ZRaporuKredi, ibanAmount));
                }

                // A) Sales Kayıtları
                // 1. Nakit Satış
                if (day.NakitSatis > 0)
                {
                    var sqlNakitSale = @"
                        INSERT INTO Sales (CustomerId, WarehouseId, SaleDate, SubTotal, VatTotal, GrandTotal, CostTotal, PaymentType)
                        VALUES (NULL, 1, @SaleDate, @GrandTotal, 0, @GrandTotal, 0, N'Nakit');";
                    await using var cmdNakit = new SqlCommand(sqlNakitSale, conn, tx);
                    cmdNakit.Parameters.AddWithValue("@SaleDate", dt.AddHours(18));
                    cmdNakit.Parameters.AddWithValue("@GrandTotal", day.NakitSatis);
                    await cmdNakit.ExecuteNonQueryAsync();
                }

                // 2. Kredi Kartı Satışı (Z Raporu tutarı kadar veya kredi satış kadar)
                decimal ccSaleAmount = (day.KrediSatis > day.ZRaporuKredi) ? day.ZRaporuKredi : day.KrediSatis;
                if (ccSaleAmount > 0)
                {
                    var sqlCcSale = @"
                        INSERT INTO Sales (CustomerId, WarehouseId, SaleDate, SubTotal, VatTotal, GrandTotal, CostTotal, PaymentType)
                        VALUES (NULL, 1, @SaleDate, @GrandTotal, 0, @GrandTotal, 0, N'Kredi Kartı');";
                    await using var cmdCc = new SqlCommand(sqlCcSale, conn, tx);
                    cmdCc.Parameters.AddWithValue("@SaleDate", dt.AddHours(19));
                    cmdCc.Parameters.AddWithValue("@GrandTotal", ccSaleAmount);
                    await cmdCc.ExecuteNonQueryAsync();
                }

                // 3. IBAN Satışı
                if (ibanAmount > 0)
                {
                    var sqlIbanSale = @"
                        INSERT INTO Sales (CustomerId, WarehouseId, SaleDate, SubTotal, VatTotal, GrandTotal, CostTotal, PaymentType)
                        VALUES (NULL, 1, @SaleDate, @GrandTotal, 0, @GrandTotal, 0, N'Banka/IBAN');";
                    await using var cmdIban = new SqlCommand(sqlIbanSale, conn, tx);
                    cmdIban.Parameters.AddWithValue("@SaleDate", dt.AddHours(19).AddMinutes(30));
                    cmdIban.Parameters.AddWithValue("@GrandTotal", ibanAmount);
                    await cmdIban.ExecuteNonQueryAsync();
                }

                // B) Kasa Oturumu ve Kasa Hareketleri
                var sqlSession = @"
                    INSERT INTO CashSessions (SessionDate, OpenedAt, OpeningCash, ClosedAt, Status, Notes)
                    VALUES (@SessionDate, @OpenedAt, 0, @ClosedAt, N'Kapandı', @Notes);
                    SELECT SCOPE_IDENTITY();";
                await using var cmdSession = new SqlCommand(sqlSession, conn, tx);
                cmdSession.Parameters.AddWithValue("@SessionDate", dt.Date);
                cmdSession.Parameters.AddWithValue("@OpenedAt", dt.AddHours(9));
                cmdSession.Parameters.AddWithValue("@ClosedAt", dt.AddHours(21));
                cmdSession.Parameters.AddWithValue("@Notes", $"Gün Sonu Kapanışı ({dt:dd.MM.yyyy})");
                long sessionId = Convert.ToInt64(await cmdSession.ExecuteScalarAsync());

                if (day.NakitSatis > 0)
                {
                    var sqlCashIn = @"
                        INSERT INTO CashTransactions (SessionId, CashAccountId, Type, Amount, Description, CreatedAt)
                        VALUES (@SessionId, 1, N'Giriş', @Amount, N'Günlük Nakit Satış (Excel Import)', @CreatedAt);";
                    await using var cmdCashIn = new SqlCommand(sqlCashIn, conn, tx);
                    cmdCashIn.Parameters.AddWithValue("@SessionId", sessionId);
                    cmdCashIn.Parameters.AddWithValue("@Amount", day.NakitSatis);
                    cmdCashIn.Parameters.AddWithValue("@CreatedAt", dt.AddHours(18));
                    await cmdCashIn.ExecuteNonQueryAsync();
                }

                if (day.NakitHarcama > 0)
                {
                    var sqlCashOut = @"
                        INSERT INTO CashTransactions (SessionId, CashAccountId, Type, Amount, Description, CreatedAt)
                        VALUES (@SessionId, 1, N'Çıkış', @Amount, N'Günlük Nakit Gider (Excel Import)', @CreatedAt);";
                    await using var cmdCashOut = new SqlCommand(sqlCashOut, conn, tx);
                    cmdCashOut.Parameters.AddWithValue("@SessionId", sessionId);
                    cmdCashOut.Parameters.AddWithValue("@Amount", day.NakitHarcama);
                    cmdCashOut.Parameters.AddWithValue("@CreatedAt", dt.AddHours(20));
                    await cmdCashOut.ExecuteNonQueryAsync();
                }

                // C) Banka ve POS Hareketleri
                // 1. IBAN Girişi
                if (ibanAmount > 0)
                {
                    var sqlBankIn = @"
                        INSERT INTO BankTransactions (CreatedAt, BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, [User])
                        VALUES (@CreatedAt, @BankId, @BankName, N'Havale', N'Giriş', @Inflow, 0, 0, @Desc, N'Havale/EFT', N'Excel Import');";
                    await using var cmdBankIn = new SqlCommand(sqlBankIn, conn, tx);
                    cmdBankIn.Parameters.AddWithValue("@CreatedAt", dt.AddHours(19).AddMinutes(30));
                    cmdBankIn.Parameters.AddWithValue("@BankId", defaultBankId);
                    cmdBankIn.Parameters.AddWithValue("@BankName", defaultBankName);
                    cmdBankIn.Parameters.AddWithValue("@Inflow", ibanAmount);
                    cmdBankIn.Parameters.AddWithValue("@Desc", $"IBAN Satış Kapanışı (Kredi Satış {day.KrediSatis:N2} - Z Raporu {day.ZRaporuKredi:N2})");
                    await cmdBankIn.ExecuteNonQueryAsync();
                }

                // 2. Kredi Kartı Harcaması (Gider)
                if (day.KrediHarcama > 0)
                {
                    var sqlBankOut = @"
                        INSERT INTO BankTransactions (CreatedAt, BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, [User])
                        VALUES (@CreatedAt, @BankId, @BankName, N'Gider', N'Çıkış', 0, @Outflow, 0, @Desc, N'Kredi Kartı', N'Excel Import');";
                    await using var cmdBankOut = new SqlCommand(sqlBankOut, conn, tx);
                    cmdBankOut.Parameters.AddWithValue("@CreatedAt", dt.AddHours(20).AddMinutes(30));
                    cmdBankOut.Parameters.AddWithValue("@BankId", defaultBankId);
                    cmdBankOut.Parameters.AddWithValue("@BankName", defaultBankName);
                    cmdBankOut.Parameters.AddWithValue("@Outflow", day.KrediHarcama);
                    cmdBankOut.Parameters.AddWithValue("@Desc", $"Kredi Kartı Harcaması (Excel Import)");
                    await cmdBankOut.ExecuteNonQueryAsync();
                }

                // 3. POS Z Raporu Kapanış Kaydı
                if (day.ZRaporuKredi > 0)
                {
                    decimal commAmt = Math.Round(day.ZRaporuKredi * 0.0399m, 2);
                    decimal netAmt = day.YatacakOlan > 0 ? day.YatacakOlan : (day.ZRaporuKredi - commAmt);

                    var sqlPos = @"
                        INSERT INTO PosTransactions (SaleId, SaleDate, GrossAmount, CommissionRate, CommissionAmount, NetAmount, ExpectedDate, ActualPaymentDate, ActualPaidAmount, BankAccountId, BankAccountName, Status, Description)
                        VALUES (NULL, @SaleDate, @Gross, 3.99, @Comm, @Net, @ExpDate, @ExpDate, @Net, @BankId, @BankName, N'Tamamlandı', @PosDesc);";
                    await using var cmdPos = new SqlCommand(sqlPos, conn, tx);
                    cmdPos.Parameters.AddWithValue("@SaleDate", dt.AddHours(19));
                    cmdPos.Parameters.AddWithValue("@Gross", day.ZRaporuKredi);
                    cmdPos.Parameters.AddWithValue("@Comm", commAmt);
                    cmdPos.Parameters.AddWithValue("@Net", netAmt);
                    cmdPos.Parameters.AddWithValue("@ExpDate", dt.AddDays(2));
                    cmdPos.Parameters.AddWithValue("@BankId", defaultBankId);
                    cmdPos.Parameters.AddWithValue("@BankName", defaultBankName);
                    cmdPos.Parameters.AddWithValue("@PosDesc", $"Z Raporu POS Kapanışı ({dt:dd.MM.yyyy})");
                    await cmdPos.ExecuteNonQueryAsync();
                }

                // D) Gün Sonu Kaydı (Closures)
                int salesCount = (day.NakitSatis > 0 ? 1 : 0) + (ccSaleAmount > 0 ? 1 : 0) + (ibanAmount > 0 ? 1 : 0);
                var sqlClosure = @"
                    INSERT INTO Closures (
                        ClosureDate, ClosedAt, SalesCount, TotalRevenue, TotalCash, TotalCreditCard, 
                        TotalOnAccount, TotalCost, TotalExpenses, TotalProfit, Notes,
                        ZReportCash, ZReportCredit, CashDiff, CreditDiff, CashExpense, CreditExpense, 
                        VatAmount, PocketCash, PocketCredit, IbanAmount, ExpectedBankPayment, 
                        Source, SourceSheet, SourceRow
                    ) VALUES (
                        @ClosureDate, @ClosedAt, @SalesCount, @TotalRevenue, @TotalCash, @TotalCreditCard,
                        0, 0, @TotalExpenses, @TotalProfit, @Notes,
                        @ZReportCash, @ZReportCredit, @CashDiff, @CreditDiff, @CashExpense, @CreditExpense,
                        @VatAmount, @PocketCash, @PocketCredit, @IbanAmount, @ExpectedBankPayment,
                        N'Excel Import', @SourceSheet, @SourceRow
                    );";
                await using var cmdClosure = new SqlCommand(sqlClosure, conn, tx);
                cmdClosure.Parameters.AddWithValue("@ClosureDate", dt.Date);
                cmdClosure.Parameters.AddWithValue("@ClosedAt", dt.AddHours(21));
                cmdClosure.Parameters.AddWithValue("@SalesCount", salesCount > 0 ? salesCount : 1);
                cmdClosure.Parameters.AddWithValue("@TotalRevenue", day.CalculatedCiro);
                cmdClosure.Parameters.AddWithValue("@TotalCash", day.NakitSatis);
                cmdClosure.Parameters.AddWithValue("@TotalCreditCard", day.KrediSatis);
                cmdClosure.Parameters.AddWithValue("@TotalExpenses", day.TotalExpenses);
                cmdClosure.Parameters.AddWithValue("@TotalProfit", day.CalculatedCiro - day.TotalExpenses);
                cmdClosure.Parameters.AddWithValue("@Notes", $"Excel: {day.SheetName} (Satır {day.RowIndex}) | IBAN: ₺{ibanAmount:N2}");
                cmdClosure.Parameters.AddWithValue("@ZReportCash", day.ZRaporuNakit);
                cmdClosure.Parameters.AddWithValue("@ZReportCredit", day.ZRaporuKredi);
                cmdClosure.Parameters.AddWithValue("@CashDiff", day.NakitFark);
                cmdClosure.Parameters.AddWithValue("@CreditDiff", day.KrediFark);
                cmdClosure.Parameters.AddWithValue("@CashExpense", day.NakitHarcama);
                cmdClosure.Parameters.AddWithValue("@CreditExpense", day.KrediHarcama);
                cmdClosure.Parameters.AddWithValue("@VatAmount", day.Kdv);
                cmdClosure.Parameters.AddWithValue("@PocketCash", day.CepNakit);
                cmdClosure.Parameters.AddWithValue("@PocketCredit", day.CepKredi);
                cmdClosure.Parameters.AddWithValue("@IbanAmount", ibanAmount);
                cmdClosure.Parameters.AddWithValue("@ExpectedBankPayment", day.YatacakOlan);
                cmdClosure.Parameters.AddWithValue("@SourceSheet", day.SheetName);
                cmdClosure.Parameters.AddWithValue("@SourceRow", day.RowIndex);
                await cmdClosure.ExecuteNonQueryAsync();

                await tx.CommitAsync();

                // İstatistikleri güncelle
                result.ImportedDays++;
                result.TotalCashSales += day.NakitSatis;
                result.TotalCreditSales += day.KrediSatis;
                result.TotalRevenue += day.CalculatedCiro;
                result.TotalCashExpenses += day.NakitHarcama;
                result.TotalCreditExpenses += day.KrediHarcama;
                result.TotalExpenses += day.TotalExpenses;
                result.TotalIbanClosures += ibanAmount;
                result.TotalZReportCredit += day.ZRaporuKredi;
                result.TotalZReportCash += day.ZRaporuNakit;
                result.TotalVat += day.Kdv;
                result.TotalExpectedBankPayment += day.YatacakOlan;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                result.ErrorCount++;
                Console.WriteLine($"[HATA] {day.Date:dd.MM.yyyy} satırı aktarılırken hata: {ex.Message}");
            }
        }

        // 6. Banka hesap bakiyelerini güncelle
        await using (var connPost = new SqlConnection(_dbService.ConnectionString))
        {
            await connPost.OpenAsync();
            var sqlUpdateBank = @"
                UPDATE BankAccounts
                SET CurrentBalance = ISNULL((SELECT SUM(Inflow) - SUM(Outflow) FROM BankTransactions WHERE BankAccountId = BankAccounts.Id), 0),
                    ActualBalance = ISNULL((SELECT SUM(Inflow) - SUM(Outflow) FROM BankTransactions WHERE BankAccountId = BankAccounts.Id), 0);
            ";
            await using var cmdUpdBank = new SqlCommand(sqlUpdateBank, connPost);
            await cmdUpdBank.ExecuteNonQueryAsync();
        }

        result.WarningCount = result.Warnings.Count;
        return result;
    }

    private static async Task InsertWarningAsync(SqlConnection conn, SqlTransaction tx, DateTime date, string sheet, int row, string type, string message)
    {
        var sql = @"
            INSERT INTO ImportWarnings (ClosureDate, SheetName, RowIndex, WarningType, Message, CreatedAt)
            VALUES (@Date, @Sheet, @Row, @Type, @Message, GETDATE());";
        await using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@Date", date.Date);
        cmd.Parameters.AddWithValue("@Sheet", (object?)sheet ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Row", row);
        cmd.Parameters.AddWithValue("@Type", type);
        cmd.Parameters.AddWithValue("@Message", message);
        await cmd.ExecuteNonQueryAsync();
    }
}
