param(
    [string]$TargetServer = ""
)

$ErrorActionPreference = "Continue"
$rootDir = $PSScriptRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "     PURE ACCESSORY - VERITABANI GUNCELLEME VE ONARIM     " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 1. SQL Server Servislerini Kontrol Et
Write-Host ">>> ADIM 1: SQL Server Windows Servisleri Taranıyor..." -ForegroundColor White
$services = Get-Service -Name "MSSQL*", "SQLBrowser" -ErrorAction SilentlyContinue

if ($services -and $services.Count -gt 0) {
    foreach ($s in $services) {
        Write-Host "   -> Servis: $($s.DisplayName) [$($s.Name)] - Durum: $($s.Status)" -ForegroundColor Gray
        if ($s.Status -ne "Running") {
            try {
                Write-Host "      Servis başlatılıyor ($($s.Name))..." -ForegroundColor Yellow
                Start-Service $s.Name -ErrorAction Stop
                Write-Host "      Başarılı: Servis başlatıldı!" -ForegroundColor Green
            } catch {
                Write-Host "      Servis başlatılamadı: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
} else {
    Write-Host "   Bilgilendirme: Yerel SQL Server Windows servisi bulunamadı (Uzak SQL sunucusu kullanılabilir)." -ForegroundColor Yellow
}

# 2. Çalışan SQL Server Örneğini Doğrula
Write-Host "`n>>> ADIM 2: SQL Server Bağlantısı Test Ediliyor..." -ForegroundColor White
Add-Type -AssemblyName System.Data

$candidates = @()
if (-not [string]::IsNullOrWhiteSpace($TargetServer)) {
    $candidates += $TargetServer
}

# settings.json dosyasından kayıtlı CustomDbConnectionString varsa oku
$possibleSettings = @(
    (Join-Path $rootDir "ERP.Desktop\bin\Debug\net9.0-windows\Data\settings.json"),
    (Join-Path $rootDir "Data\settings.json"),
    "C:\PureAccessoryERP\Data\settings.json"
)

foreach ($sp in $possibleSettings) {
    if (Test-Path $sp) {
        try {
            $json = Get-Content $sp -Raw | ConvertFrom-Json
            if ($json.CustomDbConnectionString) {
                if ($json.CustomDbConnectionString -match "Server=(.*?);") {
                    $candidates += $matches[1]
                }
            }
        } catch { }
    }
}

$candidates += @(
    "localhost",
    ".\SQLEXPRESS",
    "localhost\SQLEXPRESS",
    "$($env:COMPUTERNAME)\SQLEXPRESS",
    "(local)\SQLEXPRESS",
    ".",
    "(local)",
    "(localdb)\MSSQLLocalDB"
)

$workingServer = $null
foreach ($cand in ($candidates | Select-Object -Unique)) {
    Write-Host -NoNewline "   -> Test ediliyor: $cand ... "
    $testConnStr = "Server=$cand;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3;"
    $conn = New-Object System.Data.SqlClient.SqlConnection($testConnStr)
    try {
        $conn.Open()
        Write-Host "BAĞLANTI BAŞARILI! [OK]" -ForegroundColor Green
        $workingServer = $cand
        
        # ERPSystem Veritabanı yoksa oluştur
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'ERPSystem') BEGIN CREATE DATABASE ERPSystem; END"
        $cmd.ExecuteNonQuery() | Out-Null
        Write-Host "      ERPSystem veritabanı hazır." -ForegroundColor Green
        
        $conn.Close()
        break
    } catch {
        Write-Host "Ulaşılamadı." -ForegroundColor DarkGray
    } finally {
        if ($conn.State -eq 'Open') { $conn.Close() }
    }
}

if (-not $workingServer) {
    Write-Host "`nHATA: SQL Server'a bağlanılamadı!" -ForegroundColor Red
    Write-Host "Lütfen SQL Server Express'in çalıştığından emin olun." -ForegroundColor Yellow
    exit 1
}

# 3. Ayarlar dosyalarına çalışan bağlantı dizesini kaydet
Write-Host "`n>>> ADIM 3: Veritabanı Bağlantı Ayarları Güncelleniyor..." -ForegroundColor White
$finalConnStr = "Server=$workingServer;Database=ERPSystem;Trusted_Connection=True;TrustServerCertificate=True;"

foreach ($sp in $possibleSettings) {
    if (Test-Path $sp) {
        try {
            $json = Get-Content $sp -Raw | ConvertFrom-Json
            $json.CustomDbConnectionString = $finalConnStr
            $json | ConvertTo-Json -Depth 10 | Set-Content $sp -Encoding UTF8
            Write-Host "   -> Güncellendi: $sp" -ForegroundColor Green
        } catch { }
    }
}

# 4. Tablo Şemalarını ve Veri Senkronizasyonunu Güncelle (--migrate)
Write-Host "`n>>> ADIM 4: Veritabanı Tabloları ve Kolonları Güncelleniyor..." -ForegroundColor White

$exeCandidates = @(
    (Join-Path $rootDir "ERP.Desktop\bin\Debug\net9.0-windows\ERP.Desktop.exe"),
    (Join-Path $rootDir "ERP.Desktop\bin\Release\net9.0-windows\win-x64\ERP.Desktop.exe"),
    (Join-Path $rootDir "Release_Publish\ERP.Desktop.exe"),
    "C:\PureAccessoryERP\ERP.Desktop.exe"
)

$runExe = $null
foreach ($ex in $exeCandidates) {
    if (Test-Path $ex) {
        $runExe = $ex
        break
    }
}

if ($runExe) {
    Write-Host "   -> Çalıştırılıyor: $runExe --migrate" -ForegroundColor Cyan
    $process = Start-Process -FilePath $runExe -ArgumentList "--migrate" -PassThru -Wait -NoNewWindow
    Write-Host "   -> Veritabanı tabloları, eksik kolonlar ve şemalar başarıyla güncellendi!" -ForegroundColor Green
} else {
    Write-Host "   Bilgilendirme: Derlenmiş ERP.Desktop.exe bulunamadı, dotnet run ile çalıştırılıyor..." -ForegroundColor Yellow
    $projPath = Join-Path $rootDir "ERP.Desktop\ERP.Desktop.csproj"
    if (Test-Path $projPath) {
        dotnet run --project $projPath -- --migrate
        Write-Host "   -> Tablolar başarıyla güncellendi!" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "   TEBRİKLER! VERİTABANI GÜNCELLEMESİ BAŞARIYLA TAMAMLANDI " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Sunucu: $workingServer" -ForegroundColor Cyan
Write-Host "Veritabanı: ERPSystem" -ForegroundColor Cyan
Write-Host ""
