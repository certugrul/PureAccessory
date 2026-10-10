# =====================================================================
# Pure Accessory Otomasyon - SQL Server Veritabanı Geri Yükleme Aracı
# =====================================================================
[CmdletBinding()]
param(
    [string]$BakPath
)

# Yönetici Yetkisi Kontrolü
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Yonetici yetkisi isteniyor..." -ForegroundColor Yellow
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

Clear-Host
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "     PURE ACCESSORY ERP - VERITABANI GERI YUKLEME (DB)    " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Bak dosyasını bul
if ([string]::IsNullOrWhiteSpace($BakPath)) {
    $candidates = @(
        (Join-Path $PSScriptRoot "ERPSystem.bak"),
        (Join-Path $PSScriptRoot "..\Releases\ERPSystem.bak"),
        "C:\SQLBackup\ERPSystem.bak",
        "C:\PureAccessoryERP\ERPSystem.bak"
    )
    foreach ($cand in $candidates) {
        if (Test-Path $cand) {
            $BakPath = $cand
            break
        }
    }
}

if (-not (Test-Path $BakPath)) {
    Write-Host "HATA: 'ERPSystem.bak' dosyasi bulunamadi!" -ForegroundColor Red
    Write-Host "Lutfen 'ERPSystem.bak' dosyasini bu aracla ayni klasore veya C:\SQLBackup icine koyun." -ForegroundColor Yellow
    Read-Host "Kapatmak icin Enter'a basin"
    exit
}

Write-Host "1. Yedek dosyasi bulundu: $BakPath" -ForegroundColor White

# 2. Guvenli klasore tasi ve izin ver
$safeDir = "C:\SQLBackup"
if (-not (Test-Path $safeDir)) {
    New-Item -ItemType Directory -Path $safeDir -Force | Out-Null
}
& icacls "$safeDir" /grant "Everyone:(OI)(CI)F" /q | Out-Null

$safeBak = Join-Path $safeDir "ERPSystem.bak"
if ($BakPath -ne $safeBak) {
    Copy-Item -Path $BakPath -Destination $safeBak -Force
}
& icacls "$safeBak" /grant "Everyone:F" /q | Out-Null

# 3. SQL Server Instance Belirle
$instances = @(".\SQLEXPRESS", "localhost", "(local)")
$connectedInst = $null

foreach ($inst in $instances) {
    try {
        $c = New-Object System.Data.SqlClient.SqlConnection("Server=$inst;Database=master;Integrated Security=True;")
        $c.Open()
        $c.Close()
        $connectedInst = $inst
        break
    } catch { }
}

if (-not $connectedInst) {
    Write-Host "HATA: SQL Server'a baglanilamadi! Lutfen servisin calistigini dogrulayin." -ForegroundColor Red
    Read-Host "Kapatmak icin Enter'a basin"
    exit
}

Write-Host "2. SQL Server bulundu: $connectedInst" -ForegroundColor White
Write-Host "3. Veritabani geri yukleniyor (bu islem birkac saniye surebilir)..." -ForegroundColor Yellow

# ERP calisiyorsa kapat
Get-Process "ERP.Desktop" -ErrorAction SilentlyContinue | Stop-Process -Force

try {
    $conn = New-Object System.Data.SqlClient.SqlConnection("Server=$connectedInst;Database=master;Integrated Security=True;")
    $conn.Open()
    
    # Eger bagli kullanici varsa baglantilari kopar
    $sqlKill = @"
IF EXISTS (SELECT name FROM sys.databases WHERE name = N'ERPSystem')
BEGIN
    ALTER DATABASE [ERPSystem] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
END
"@
    $cmdKill = $conn.CreateCommand()
    $cmdKill.CommandText = $sqlKill
    $cmdKill.ExecuteNonQuery() | Out-Null

    # Restore calistir - Hedef bilgisayarin kendi SQL DATA klasorunu otomatik bularak MOVE uygula
    $sqlRestore = @"
DECLARE @dataDir NVARCHAR(512) = CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(512));
DECLARE @logDir NVARCHAR(512) = CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS NVARCHAR(512));

IF @dataDir IS NULL OR LEN(@dataDir) = 0
BEGIN
    SELECT TOP 1 @dataDir = LEFT(physical_name, LEN(physical_name) - CHARINDEX('\', REVERSE(physical_name)) + 1)
    FROM master.sys.master_files WHERE database_id = 1 AND file_id = 1;
END

IF @logDir IS NULL OR LEN(@logDir) = 0
BEGIN
    SELECT TOP 1 @logDir = LEFT(physical_name, LEN(physical_name) - CHARINDEX('\', REVERSE(physical_name)) + 1)
    FROM master.sys.master_files WHERE database_id = 1 AND file_id = 2;
END

IF RIGHT(@dataDir, 1) <> '\' SET @dataDir = @dataDir + '\';
IF RIGHT(@logDir, 1) <> '\' SET @logDir = @logDir + '\';

DECLARE @mdf NVARCHAR(512) = @dataDir + 'ERPSystem.mdf';
DECLARE @ldf NVARCHAR(512) = @logDir + 'ERPSystem_log.ldf';

DECLARE @sql NVARCHAR(MAX) = N'
RESTORE DATABASE [ERPSystem]
FROM DISK = N''' + REPLACE(N'$safeBak', '''', '''''') + N'''
WITH REPLACE, RECOVERY,
MOVE N''ERPSystem'' TO N''' + @mdf + N''',
MOVE N''ERPSystem_log'' TO N''' + @ldf + N''';
';

EXEC sp_executesql @sql;
"@
    $cmdRestore = $conn.CreateCommand()
    $cmdRestore.CommandTimeout = 90
    $cmdRestore.CommandText = $sqlRestore
    $cmdRestore.ExecuteNonQuery() | Out-Null

    # Multi user yap
    $sqlMulti = @"
ALTER DATABASE [ERPSystem] SET MULTI_USER;
"@
    $cmdMulti = $conn.CreateCommand()
    $cmdMulti.CommandText = $sqlMulti
    $cmdMulti.ExecuteNonQuery() | Out-Null

    $conn.Close()

    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "       VERITABANI BASARIYLA GERI YUKLENDI!                " -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "Tum urunler, stoklar, cariler ve finans verileri eksiksiz yuklendi." -ForegroundColor White
} catch {
    Write-Host "HATA olustu: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Read-Host "Kapatmak icin Enter tusuna basin..."
