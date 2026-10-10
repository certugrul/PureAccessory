# =====================================================================
# Pure Accessory - SQL Server Otomatik Teşhis ve Onarma Aracı
# =====================================================================
[CmdletBinding()]
param()

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Yonetici yetkisi isteniyor..." -ForegroundColor Yellow
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

Clear-Host
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "     PURE ACCESSORY - SQL SERVER KONTROL & ONARIM         " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "1. SQL Server Windows Servisleri Taranıyor..." -ForegroundColor White
$services = Get-Service -Name "MSSQL*", "SQLBrowser" -ErrorAction SilentlyContinue

if (-not $services -or $services.Count -eq 0) {
    Write-Host "UYARI: Bu bilgisayarda yuklu herhangi bir SQL Server servisi bulunamadi!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Lutfen SQL Server Express kurulumunu tamamladiginizdan emin olun." -ForegroundColor Yellow
    Write-Host "Indirme linki: https://www.microsoft.com/tr-tr/sql-server/sql-server-downloads" -ForegroundColor Yellow
    Write-Host ""
    pause
    exit
}

foreach ($s in $services) {
    Write-Host "   -> Servis: $($s.DisplayName) [$($s.Name)] - Durum: $($s.Status)" -ForegroundColor Cyan
    if ($s.Status -ne "Running") {
        Write-Host "      Servis baslatiliyor..." -ForegroundColor Yellow
        try {
            Start-Service $s.Name -ErrorAction Stop
            Write-Host "      Basarili: Servis baslatildi!" -ForegroundColor Green
        } catch {
            Write-Host "      Servis baslatilamadi: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "2. Yerel SQL Server Örnekleri Test Ediliyor..." -ForegroundColor White

$candidates = @(
    ".\SQLEXPRESS",
    "localhost\SQLEXPRESS",
    "$($env:COMPUTERNAME)\SQLEXPRESS",
    "(local)\SQLEXPRESS",
    "localhost",
    ".",
    "(local)",
    "(localdb)\MSSQLLocalDB"
)

$workingInstance = $null

# SqlClient Assembly Yükle
Add-Type -AssemblyName System.Data

foreach ($cand in $candidates) {
    Write-Host -NoNewline "   -> Test ediliyor: $cand ... "
    $connStr = "Server=$cand;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3;"
    $conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
    try {
        $conn.Open()
        Write-Host "BAGLANTI BASARILI! [OK]" -ForegroundColor Green
        $workingInstance = $cand
        
        # ERPSystem Veritabanını Kontrol Et / Oluştur
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'ERPSystem') BEGIN CREATE DATABASE ERPSystem; END"
        $cmd.ExecuteNonQuery() | Out-Null
        Write-Host "      ERPSystem veritabanı doğrulandı/oluşturuldu." -ForegroundColor Green
        
        $conn.Close()
        break
    } catch {
        Write-Host "Ulasilamadi." -ForegroundColor DarkGray
    } finally {
        if ($conn.State -eq 'Open') { $conn.Close() }
    }
}

Write-Host ""
if ($workingInstance) {
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "   TEBRIKLER! SQL SERVER BASARIYLA CALISIYOR VE BAGLANDI  " -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "Calisan Sunucu Adresi: $workingInstance" -ForegroundColor Cyan
    Write-Host "Veritabani: ERPSystem" -ForegroundColor Cyan
    Write-Host ""
    
    # settings.json dosyasını güncelle
    $settingsPath = "C:\PureAccessoryERP\Data\settings.json"
    if (Test-Path $settingsPath) {
        try {
            $jsonContent = Get-Content $settingsPath -Raw | ConvertFrom-Json
            $jsonContent.CustomDbConnectionString = "Server=$workingInstance;Database=ERPSystem;Trusted_Connection=True;TrustServerCertificate=True;"
            $jsonContent | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
            Write-Host "Ayarlar dosyasi ($settingsPath) otomatik guncellendi!" -ForegroundColor Green
        } catch { }
    }
} else {
    Write-Host "==========================================================" -ForegroundColor Red
    Write-Host "   HICBIR YEREL SQL SERVER ORNEGI BAGLANTI KABUL ETMEDI   " -ForegroundColor Red
    Write-Host "==========================================================" -ForegroundColor Red
    Write-Host "Olası Nedenler:" -ForegroundColor Yellow
    Write-Host "1. SQL Server kurulumu tamamlanmamis veya iptal edilmis olabilir." -ForegroundColor Yellow
    Write-Host "2. Windows kullanici hesabinizin SQL Server yetkisi olmayabilir." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Islem tamamlandi. Pencereyi kapatabilirsiniz." -ForegroundColor White
pause
