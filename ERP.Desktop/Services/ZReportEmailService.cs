using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

public class ZReportEmailService
{
    private readonly DatabaseService _dbService;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string StorageFile = Path.Combine(StorageDirectory, "zreport_emails.json");
    private static readonly string PdfDirectory = Path.Combine(StorageDirectory, "Reports", "ZReports");
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public ZReportEmailService(DatabaseService? dbService = null)
    {
        _dbService = dbService ?? new DatabaseService();
        EnsureDirectories();
    }

    private static void EnsureDirectories()
    {
        try
        {
            if (!Directory.Exists(StorageDirectory)) Directory.CreateDirectory(StorageDirectory);
            if (!Directory.Exists(PdfDirectory)) Directory.CreateDirectory(PdfDirectory);
        }
        catch { }
    }

    /// <summary>
    /// Belirtilen güne veya kapanışa ait Z Raporu PDF'ini oluşturup yapılandırılan e-posta adresine gönderir.
    /// Gün sonu finansal işlemlerini bloke etmez veya geriye almaz; sonuç durumunu veritabanında saklar.
    /// </summary>
    public async Task<bool> SendZReportEmailAsync(long? closureId, DateTime reportDate, bool isRetry = false)
    {
        await _lock.WaitAsync();
        try
        {
            var settings = ThemeService.Current ?? new AppSettings();

            // Mükerrer gönderim koruması: Zaten başarıyla gönderilmişse tekrar gönderme
            if (!isRetry && await IsAlreadySentAsync(closureId, reportDate))
            {
                System.Diagnostics.Debug.WriteLine($"[ZReportEmailService] {reportDate:yyyy-MM-dd} Z Raporu zaten başarıyla gönderilmiş. Mükerrer gönderim engellendi.");
                return true;
            }

            string recipient = settings.EmailRecipient?.Trim() ?? "";
            string sender = string.IsNullOrWhiteSpace(settings.EmailSender) ? recipient : settings.EmailSender.Trim();
            string senderName = string.IsNullOrWhiteSpace(settings.EmailSenderName) ? "ERP Gün Sonu Sistemi" : settings.EmailSenderName.Trim();
            string host = settings.EmailSmtpHost?.Trim() ?? "";
            int port = settings.EmailSmtpPort > 0 ? settings.EmailSmtpPort : 587;
            bool enableSsl = settings.EmailEnableSsl;
            string username = settings.EmailSmtpUser?.Trim() ?? "";
            string password = SecurityHelper.DecryptString(settings.EmailSmtpPasswordEncrypted ?? "");

            string subject = $"Gün Sonu Z Raporu - Pure Accessory - {reportDate:dd.MM.yyyy}";
            string pdfFileName = $"Z_Raporu_{reportDate:yyyy-MM-dd}.pdf";
            string pdfPath = Path.Combine(PdfDirectory, pdfFileName);

            // Kuyruk kaydı oluştur veya güncelle (Gönderiliyor durumuna al)
            long emailRecordId = await LogOrUpdateEmailStatusAsync(closureId, reportDate, recipient, subject, pdfPath, "Gönderiliyor", null);

            // Temel SMTP ayarları kontrolü
            if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(host))
            {
                string missingConfigErr = "E-posta alıcı adresi veya SMTP sunucu adresi tanımlı değil. Lütfen Ayarlar > E-Posta bölümünü yapılandırın.";
                await LogOrUpdateEmailStatusAsync(closureId, reportDate, recipient, subject, pdfPath, "Başarısız", missingConfigErr, emailRecordId);
                return false;
            }

            // 1. Z Raporu verilerini topla
            var reportDataService = new ReportDataService();
            var zData = await reportDataService.GetZReportDataAsync(reportDate);
            if (zData == null)
            {
                string noDataErr = $"{reportDate:dd.MM.yyyy} tarihine ait kaydedilmiş Z Raporu veya gün sonu bulunamadı.";
                await LogOrUpdateEmailStatusAsync(closureId, reportDate, recipient, subject, pdfPath, "Başarısız", noDataErr, emailRecordId);
                return false;
            }

            if (closureId.HasValue && closureId.Value > 0)
            {
                zData.ClosureId = closureId.Value;
                zData.ReportNumber = $"Z-{closureId.Value:D6}";
            }

            // 2. QuestPDF ile profesyonel Türkçe A4 Z Raporu PDF oluştur ve diske kaydet
            var pdfService = new PdfReportService();
            string generatedPdfPath = pdfService.GenerateZReportPdf(zData, pdfPath);
            byte[] pdfBytes = await File.ReadAllBytesAsync(generatedPdfPath);

            // 3. HTML E-Posta gövdesini oluştur
            string htmlBody = GenerateEmailHtmlBody(zData);

            // 4. SMTP ile gönderim
            using var mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(sender, senderName);
            mailMessage.To.Add(recipient);
            mailMessage.Subject = subject;
            mailMessage.Body = htmlBody;
            mailMessage.IsBodyHtml = true;

            using var pdfStream = new MemoryStream(pdfBytes);
            using var attachment = new Attachment(pdfStream, pdfFileName, "application/pdf");
            mailMessage.Attachments.Add(attachment);

            using var smtpClient = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Timeout = 30000 // 30 saniye
            };

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                smtpClient.UseDefaultCredentials = false;
                smtpClient.Credentials = new NetworkCredential(username, password);
            }
            else
            {
                smtpClient.UseDefaultCredentials = true;
            }

            await smtpClient.SendMailAsync(mailMessage);

            // 5. Başarılı gönderim olarak kaydet
            await LogOrUpdateEmailStatusAsync(closureId, reportDate, recipient, subject, generatedPdfPath, "Gönderildi", null, emailRecordId, DateTime.Now);
            System.Diagnostics.Debug.WriteLine($"[ZReportEmailService] {reportDate:yyyy-MM-dd} Z Raporu başarıyla {recipient} adresine iletildi.");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ZReportEmailService] E-posta gönderim hatası: {ex.Message}");
            await LogOrUpdateEmailStatusAsync(closureId, reportDate, "", "", null, "Başarısız", ex.Message);
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Ayarlar ekranından test e-postası gönderir.
    /// </summary>
    public static async Task<(bool success, string message)> SendTestEmailAsync(
        string recipient, 
        string sender, 
        string senderName, 
        string host, 
        int port, 
        bool enableSsl, 
        string username, 
        string password)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(recipient))
                return (false, "Alıcı e-posta adresi boş olamaz.");
            if (string.IsNullOrWhiteSpace(host))
                return (false, "SMTP sunucu adresi boş olamaz.");

            string fromEmail = string.IsNullOrWhiteSpace(sender) ? recipient : sender.Trim();
            string fromName = string.IsNullOrWhiteSpace(senderName) ? "ERP Test Sistemi" : senderName.Trim();

            using var mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(fromEmail, fromName);
            mailMessage.To.Add(recipient.Trim());
            mailMessage.Subject = $"ERP Z Raporu E-Posta Test Bildirimi - {DateTime.Now:dd.MM.yyyy HH:mm}";
            mailMessage.IsBodyHtml = true;
            mailMessage.Body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'/>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f8fafc; margin: 0; padding: 20px; }}
        .card {{ background: #ffffff; max-width: 600px; margin: 0 auto; border-radius: 8px; border: 1px solid #e2e8f0; padding: 24px; box-shadow: 0 4px 6px rgba(0,0,0,0.05); }}
        .header {{ border-bottom: 2px solid #3b82f6; padding-bottom: 12px; margin-bottom: 16px; }}
        h2 {{ color: #1e293b; margin: 0; }}
        p {{ color: #475569; font-size: 14px; line-height: 1.6; }}
        .badge {{ display: inline-block; background-color: #10b981; color: white; padding: 4px 10px; border-radius: 4px; font-weight: bold; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='card'>
        <div class='header'>
            <h2>Pure Accessory - ERP E-Posta Bağlantı Testi</h2>
        </div>
        <p><span class='badge'>BAŞARILI</span></p>
        <p>Tebrikler! ERP sisteminizin SMTP e-posta yapılandırması başarıyla doğrulandı.</p>
        <p>Gün sonu Z Raporları alındığında otomatik olarak bu e-posta adresine PDF ekiyle birlikte ulaştırılacaktır.</p>
        <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 20px 0;'/>
        <p style='font-size: 12px; color: #94a3b8;'>Test Tarihi: {DateTime.Now:dd.MM.yyyy HH:mm:ss} | Sunucu: {host}:{port}</p>
    </div>
</body>
</html>";

            using var smtpClient = new SmtpClient(host.Trim(), port)
            {
                EnableSsl = enableSsl,
                Timeout = 20000
            };

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                smtpClient.UseDefaultCredentials = false;
                smtpClient.Credentials = new NetworkCredential(username.Trim(), password);
            }
            else
            {
                smtpClient.UseDefaultCredentials = true;
            }

            await smtpClient.SendMailAsync(mailMessage);
            return (true, "Test e-postası başarıyla gönderildi!");
        }
        catch (Exception ex)
        {
            return (false, $"Test gönderimi başarısız oldu: {ex.Message}");
        }
    }

    /// <summary>
    /// Önceden bekleyen veya başarısız olan e-postaları yeniden kuyruktan dener.
    /// </summary>
    public async Task ProcessPendingQueueAsync()
    {
        try
        {
            var pending = await GetPendingOrFailedEmailsAsync();
            foreach (var item in pending)
            {
                if (item.RetryCount < 3)
                {
                    await SendZReportEmailAsync(item.ClosureId, item.ReportDate, isRetry: true);
                }
            }
        }
        catch { }
    }

    private async Task<bool> IsAlreadySentAsync(long? closureId, DateTime reportDate)
    {
        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                string sql = @"
                    SELECT COUNT(1) 
                    FROM ZReportEmails 
                    WHERE (ClosureId = @ClosureId OR ReportDate = @ReportDate) 
                      AND Status = 'Gönderildi'";

                await using var cmd = conn.CreateCommand(sql);
                cmd.AddParam("ClosureId", closureId ?? (object)DBNull.Value);
                cmd.AddParam("ReportDate", reportDate.Date);

                int count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count > 0) return true;
            }
            catch { }
        }

        // Yerel JSON kontrolü
        var localList = await LoadLocalEmailsAsync();
        return localList.Any(x => 
            (closureId.HasValue && x.ClosureId == closureId.Value || x.ReportDate.Date == reportDate.Date) && 
            x.Status == "Gönderildi");
    }

    private async Task<long> LogOrUpdateEmailStatusAsync(
        long? closureId, 
        DateTime reportDate, 
        string recipient, 
        string subject, 
        string? pdfPath, 
        string status, 
        string? errorMessage, 
        long existingRecordId = 0,
        DateTime? sentAt = null)
    {
        long recordId = existingRecordId;

        // 1. SQL Server Kaydı
        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                if (recordId > 0)
                {
                    string updateSql = @"
                        UPDATE ZReportEmails 
                        SET Status = @Status, 
                            ErrorMessage = @ErrorMessage, 
                            SentAt = @SentAt,
                            PdfFilePath = COALESCE(@PdfFilePath, PdfFilePath),
                            RetryCount = RetryCount + CASE WHEN @Status = 'Başarısız' THEN 1 ELSE 0 END
                        WHERE Id = @Id";

                    await using var cmd = conn.CreateCommand(updateSql);
                    cmd.AddParam("Id", recordId);
                    cmd.AddParam("Status", status);
                    cmd.AddParam("ErrorMessage", errorMessage ?? (object)DBNull.Value);
                    cmd.AddParam("SentAt", sentAt ?? (object)DBNull.Value);
                    cmd.AddParam("PdfFilePath", pdfPath ?? (object)DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }
                else
                {
                    string insertSql = @"
                        INSERT INTO ZReportEmails (ClosureId, ReportDate, RecipientEmail, Subject, PdfFilePath, Status, ErrorMessage, RetryCount, SentAt, CreatedAt)
                        VALUES (@ClosureId, @ReportDate, @RecipientEmail, @Subject, @PdfFilePath, @Status, @ErrorMessage, 0, @SentAt, GETDATE());
                        SELECT SCOPE_IDENTITY();";

                    await using var cmd = conn.CreateCommand(insertSql);
                    cmd.AddParam("ClosureId", closureId ?? (object)DBNull.Value);
                    cmd.AddParam("ReportDate", reportDate.Date);
                    cmd.AddParam("RecipientEmail", recipient);
                    cmd.AddParam("Subject", subject);
                    cmd.AddParam("PdfFilePath", pdfPath ?? (object)DBNull.Value);
                    cmd.AddParam("Status", status);
                    cmd.AddParam("ErrorMessage", errorMessage ?? (object)DBNull.Value);
                    cmd.AddParam("SentAt", sentAt ?? (object)DBNull.Value);

                    var idObj = await cmd.ExecuteScalarAsync();
                    if (idObj != null && idObj != DBNull.Value)
                    {
                        recordId = Convert.ToInt64(idObj);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ZReportEmailService] DB Log Hatası: {ex.Message}");
            }
        }

        // 2. Yerel JSON Senkronizasyonu
        try
        {
            var localList = await LoadLocalEmailsAsync();
            var existing = localList.FirstOrDefault(x => 
                (recordId > 0 && x.Id == recordId) || 
                (closureId.HasValue && x.ClosureId == closureId.Value) || 
                x.ReportDate.Date == reportDate.Date);

            if (existing != null)
            {
                existing.Status = status;
                existing.ErrorMessage = errorMessage;
                if (sentAt.HasValue) existing.SentAt = sentAt;
                if (!string.IsNullOrEmpty(pdfPath)) existing.PdfFilePath = pdfPath;
                if (status == "Başarısız") existing.RetryCount++;
            }
            else
            {
                localList.Add(new ZReportEmailRecord
                {
                    Id = recordId > 0 ? recordId : DateTime.Now.Ticks,
                    ClosureId = closureId,
                    ReportDate = reportDate.Date,
                    RecipientEmail = recipient,
                    Subject = subject,
                    PdfFilePath = pdfPath,
                    Status = status,
                    ErrorMessage = errorMessage,
                    RetryCount = status == "Başarısız" ? 1 : 0,
                    SentAt = sentAt,
                    CreatedAt = DateTime.Now
                });
            }

            await SaveLocalEmailsAsync(localList);
        }
        catch { }

        return recordId;
    }

    public async Task<List<ZReportEmailRecord>> GetEmailHistoryAsync()
    {
        bool dbConnected = await _dbService.TestConnectionAsync();
        if (dbConnected)
        {
            try
            {
                await using var conn = _dbService.CreateConnection();
                await conn.OpenAsync();

                string sql = "SELECT TOP 50 * FROM ZReportEmails ORDER BY CreatedAt DESC";
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync();

                var list = new List<ZReportEmailRecord>();
                while (await reader.ReadAsync())
                {
                    list.Add(new ZReportEmailRecord
                    {
                        Id = reader.GetInt64(reader.GetOrdinal("Id")),
                        ClosureId = reader.IsDBNull(reader.GetOrdinal("ClosureId")) ? null : reader.GetInt64(reader.GetOrdinal("ClosureId")),
                        ReportDate = reader.GetDateTime(reader.GetOrdinal("ReportDate")),
                        RecipientEmail = reader.GetString(reader.GetOrdinal("RecipientEmail")),
                        Subject = reader.GetString(reader.GetOrdinal("Subject")),
                        PdfFilePath = reader.IsDBNull(reader.GetOrdinal("PdfFilePath")) ? null : reader.GetString(reader.GetOrdinal("PdfFilePath")),
                        Status = reader.GetString(reader.GetOrdinal("Status")),
                        ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? null : reader.GetString(reader.GetOrdinal("ErrorMessage")),
                        RetryCount = reader.GetInt32(reader.GetOrdinal("RetryCount")),
                        SentAt = reader.IsDBNull(reader.GetOrdinal("SentAt")) ? null : reader.GetDateTime(reader.GetOrdinal("SentAt")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                    });
                }
                return list;
            }
            catch { }
        }

        return await LoadLocalEmailsAsync();
    }

    private async Task<List<ZReportEmailRecord>> GetPendingOrFailedEmailsAsync()
    {
        var all = await GetEmailHistoryAsync();
        return all.Where(x => x.Status == "Bekliyor" || x.Status == "Başarısız").ToList();
    }

    private static async Task<List<ZReportEmailRecord>> LoadLocalEmailsAsync()
    {
        try
        {
            if (!File.Exists(StorageFile)) return new List<ZReportEmailRecord>();
            string json = await File.ReadAllTextAsync(StorageFile);
            return JsonSerializer.Deserialize<List<ZReportEmailRecord>>(json) ?? new List<ZReportEmailRecord>();
        }
        catch { return new List<ZReportEmailRecord>(); }
    }

    private static async Task SaveLocalEmailsAsync(List<ZReportEmailRecord> list)
    {
        try
        {
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(StorageFile, json);
        }
        catch { }
    }

    private static string GenerateEmailHtmlBody(ZReportData d)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'/>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 20px; color: #1e293b; }}
        .container {{ max-width: 650px; margin: 0 auto; background: #ffffff; border-radius: 10px; overflow: hidden; border: 1px solid #cbd5e1; box-shadow: 0 4px 12px rgba(0,0,0,0.06); }}
        .header {{ background-color: #1e3a8a; color: #ffffff; padding: 24px; text-align: center; }}
        .header h1 {{ margin: 0 0 6px 0; font-size: 22px; font-weight: 700; letter-spacing: 0.5px; }}
        .header p {{ margin: 0; font-size: 13px; color: #bfdbfe; }}
        .content {{ padding: 24px; }}
        .grid {{ display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 20px; }}
        .stat-card {{ background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 12px 16px; }}
        .stat-label {{ font-size: 11px; text-transform: uppercase; color: #64748b; font-weight: 600; margin-bottom: 4px; }}
        .stat-val {{ font-size: 18px; font-weight: 700; color: #0f172a; }}
        .stat-val.revenue {{ color: #15803d; }}
        .stat-val.cash {{ color: #0369a1; }}
        .stat-val.card {{ color: #7c3aed; }}
        .table-section {{ margin-top: 20px; }}
        .table-section h3 {{ font-size: 14px; margin: 0 0 8px 0; color: #334155; border-bottom: 1px solid #e2e8f0; padding-bottom: 6px; }}
        table {{ width: 100%; border-collapse: collapse; font-size: 13px; }}
        th, td {{ padding: 8px 10px; text-align: left; border-bottom: 1px solid #f1f5f9; }}
        th {{ background-color: #f8fafc; color: #475569; font-weight: 600; }}
        .text-right {{ text-align: right; }}
        .footer {{ background: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; font-size: 12px; color: #64748b; text-align: center; line-height: 1.5; }}
        .badge-notice {{ background: #e0f2fe; color: #0369a1; padding: 8px 12px; border-radius: 6px; font-size: 12px; margin-bottom: 16px; border-left: 4px solid #0284c7; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>{d.StoreName}</h1>
            <p>GÜN SONU Z RAPORU BİLDİRİMİ | {d.ClosureDate:dd.MM.yyyy}</p>
        </div>
        <div class='content'>
            <div class='badge-notice'>
                ℹ️ Bu e-posta, <strong>{d.ClosureDate:dd.MM.yyyy}</strong> tarihli gün sonu işlemi tamamlandığında otomatik olarak oluşturulmuştur. Resmi Z Raporu PDF belgesi e-postaya eklenmiştir.
            </div>

            <div class='grid'>
                <div class='stat-card'>
                    <div class='stat-label'>Toplam Satış / Ciro</div>
                    <div class='stat-val revenue'>{d.TotalRevenue:N2} ₺</div>
                </div>
                <div class='stat-card'>
                    <div class='stat-label'>Fiş / Satış Adedi</div>
                    <div class='stat-val'>{d.SalesCount} Adet</div>
                </div>
                <div class='stat-card'>
                    <div class='stat-label'>Nakit Tahsilat</div>
                    <div class='stat-val cash'>{d.TotalCash:N2} ₺</div>
                </div>
                <div class='stat-card'>
                    <div class='stat-label'>POS / Kredi Kartı</div>
                    <div class='stat-val card'>{d.TotalCreditCard:N2} ₺</div>
                </div>
                <div class='stat-card'>
                    <div class='stat-label'>Kasa Kapanış Bakiyesi</div>
                    <div class='stat-val'>{d.ClosingCashBalance:N2} ₺</div>
                </div>
                <div class='stat-card'>
                    <div class='stat-label'>Bekleyen POS Alacakları</div>
                    <div class='stat-val'>{d.PendingPosAmount:N2} ₺</div>
                </div>
            </div>

            <div class='table-section'>
                <h3>Finansal Mutabakat ve Kasa Özeti</h3>
                <table>
                    <tr><td>Kasa Açılış Tutarı</td><td class='text-right'><strong>{d.OpeningCash:N2} ₺</strong></td></tr>
                    <tr><td>Gün İçi Nakit Girişleri</td><td class='text-right'>{d.CashInflows:N2} ₺</td></tr>
                    <tr><td>Gün İçi Nakit Giderleri / Çıkışları</td><td class='text-right' style='color:#dc2626;'>-{d.CashOutflows:N2} ₺</td></tr>
                    <tr><td>Z Raporu Nakit vs Sistem Kasa Farkı</td><td class='text-right'><strong>{d.CashDiff:N2} ₺</strong></td></tr>
                    <tr><td>Z Raporu Kredi vs Sistem Kart Farkı</td><td class='text-right'><strong>{d.CreditDiff:N2} ₺</strong></td></tr>
                    <tr><td>IBAN Kapanışı (Havale/EFT)</td><td class='text-right'>{d.IbanAmount:N2} ₺</td></tr>
                    <tr><td>Beklenen Banka Ödemesi</td><td class='text-right'>{d.ExpectedBankPayment:N2} ₺</td></tr>
                </table>
            </div>
        </div>
        <div class='footer'>
            Pure Accessory ERP Finans ve Kasa Yönetim Sistemi<br/>
            Bu rapor dahili yönetim ve denetim amacıyla üretilmiştir. Rapor No: {d.ReportNumber}
        </div>
    </div>
</body>
</html>";
    }
}
