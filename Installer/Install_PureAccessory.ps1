# =====================================================================
# Pure Accessory Otomasyon - Otomatik Kurulum ve İzin Tanımlayıcı Script
# =====================================================================
[CmdletBinding()]
param()

# Yönetici Yetkisi Kontrolü ve Yükseltme
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Yonetici yetkisi isteniyor..." -ForegroundColor Yellow
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

Clear-Host
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "    PURE ACCESSORY OTOMASYON - OTOMATIK KURULUM SISTEMI   " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

$installDir = "C:\PureAccessoryERP"
$sourceDir = Join-Path $PSScriptRoot "AppFiles"
if (-not (Test-Path $sourceDir)) {
    # Eğer aynı klasörde dosyalar varsa
    $sourceDir = $PSScriptRoot
}

Write-Host "1. Kurulum dizini hazirlaniyor: $installDir" -ForegroundColor White
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

Write-Host "2. Dosyalar kopyalaniyor..." -ForegroundColor White
# Calisan ERP varsa kapat
Get-Process "ERP.Desktop" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process "Updater" -ErrorAction SilentlyContinue | Stop-Process -Force

Copy-Item -Path "$sourceDir\*" -Destination $installDir -Recurse -Force -Exclude "Install_PureAccessory.ps1", "Kurulum_Yap.bat"

Write-Host "3. Tum kullanicilara tam okuma/yazma izinleri tanimlaniyor..." -ForegroundColor White
try {
    # Tum kullanicilara ve Everyone grubuna tam yetki ver (UAC ve erisim engellerini kaldirir)
    & icacls "$installDir" /grant "Everyone:(OI)(CI)F" /t /q | Out-Null
    & icacls "$installDir" /grant "Users:(OI)(CI)F" /t /q | Out-Null
} catch {
    Write-Host "Izin atama uyarisi: $($_.Exception.Message)" -ForegroundColor DarkGray
}

Write-Host "4. Windows Guvenlik Duvari kurallari ekleniyor..." -ForegroundColor White
try {
    $mainExe = Join-Path $installDir "ERP.Desktop.exe"
    $tunnelExe = Join-Path $installDir "Tools\cloudflared.exe"
    
    & netsh advfirewall firewall delete rule name="Pure Accessory ERP" | Out-Null
    & netsh advfirewall firewall add rule name="Pure Accessory ERP" dir=in action=allow program="$mainExe" enable=yes | Out-Null
    
    if (Test-Path $tunnelExe) {
        & netsh advfirewall firewall delete rule name="Pure Accessory Tunnel" | Out-Null
        & netsh advfirewall firewall add rule name="Pure Accessory Tunnel" dir=in action=allow program="$tunnelExe" enable=yes | Out-Null
    }
} catch {
    Write-Host "Guvenlik duvari ayari uyarisi: $($_.Exception.Message)" -ForegroundColor DarkGray
}

Write-Host "5. SQL Server servisleri kontrol ediliyor..." -ForegroundColor White
try {
    Get-Service -Name "MSSQL*", "SQLBrowser" -ErrorAction SilentlyContinue | Where-Object { $_.Status -ne "Running" } | Start-Service -ErrorAction SilentlyContinue
} catch { }

Write-Host "5. Masaustu ve Baslat Menusu kisayollari olusturuluyor..." -ForegroundColor White
try {
    $wshShell = New-Object -ComObject WScript.Shell
    $desktopPath = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktopPath "Pure Accessory Otomasyon.lnk"
    $shortcut = $wshShell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $installDir "ERP.Desktop.exe"
    $shortcut.WorkingDirectory = $installDir
    
    $iconPath = Join-Path $installDir "Assets\logo.ico"
    if (Test-Path $iconPath) {
        $shortcut.IconLocation = "$iconPath,0"
    }
    $shortcut.Save()

    # Baslat Menusu Kisayolu
    $startMenuPrograms = [Environment]::GetFolderPath("CommonPrograms")
    $startShortcutPath = Join-Path $startMenuPrograms "Pure Accessory Otomasyon.lnk"
    $startShortcut = $wshShell.CreateShortcut($startShortcutPath)
    $startShortcut.TargetPath = Join-Path $installDir "ERP.Desktop.exe"
    $startShortcut.WorkingDirectory = $installDir
    if (Test-Path $iconPath) {
        $startShortcut.IconLocation = "$iconPath,0"
    }
    $startShortcut.Save()
} catch {
    Write-Host "Kisayol olusturma uyarisi: $($_.Exception.Message)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "       KURULUM BASARIYLA TAMAMLANDI! UYGULAMA ACILIYOR... " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host ""

Start-Process (Join-Path $installDir "ERP.Desktop.exe") -WorkingDirectory $installDir
Start-Sleep -Seconds 2
