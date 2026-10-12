param(
    [string]$Version = "",
    [string]$Message = ""
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   PURE ACCESSORY - TEK TIKLA GUNCELLE & YAYINLA (VS + GIT) " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# Eğer sürüm belirtilmemişse mevcut sürümü oku ve bir artır
$desktopProj = Join-Path $rootDir "ERP.Desktop\ERP.Desktop.csproj"
$currentVersion = "1.0.8"
if (Test-Path $desktopProj) {
    $content = Get-Content $desktopProj -Raw
    if ($content -match "<Version>(.*?)</Version>") {
        $currentVersion = $matches[1]
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    # Otomatik revizyon artırımı (örn: 1.0.8 -> 1.0.9)
    $parts = $currentVersion.Split('.')
    if ($parts.Count -eq 3) {
        $parts[2] = [string]([int]$parts[2] + 1)
        $Version = $parts -join '.'
    } else {
        $Version = "$currentVersion.1"
    }
    Write-Host "Mevcut Sürüm: v$currentVersion -> Yeni Hedef Sürüm: v$Version" -ForegroundColor Yellow
} else {
    Write-Host "Hedef Sürüm: v$Version" -ForegroundColor Yellow
}

if ([string]::IsNullOrWhiteSpace($Message)) {
    $Message = "Pure Accessory v$Version guncellemesi"
}

# 1. Visual Studio csproj dosyalarını güncelle ve Release paketini derle
Write-Host "`n>>> ADIM 1: Sürüm derleniyor ve paketleniyor..." -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File (Join-Path $rootDir "Build_Release_And_Setup.ps1") -Version $Version -ChangeLog $Message
if ($LASTEXITCODE -ne 0) {
    Write-Host "Paketleme başarısız oldu!" -ForegroundColor Red
    exit 1
}

# 2. Değişen kaynak kodları Git'e ekle, commit et
Write-Host "`n>>> ADIM 2: Kaynak kodlar Visual Studio ve Git ortamına kaydediliyor..." -ForegroundColor Cyan
git add .
$status = git status --porcelain
if ($status) {
    git commit -m "feat: release v$Version - $Message"
    Write-Host "   -> Git commit tamamlandı!" -ForegroundColor Green
} else {
    Write-Host "   -> Değişiklik yok veya zaten commit edilmiş." -ForegroundColor Gray
}

# 3. GitHub'a Push et ve GitHub Release olarak ZIP paketini yayınla
Write-Host "`n>>> ADIM 3: GitHub'a push ediliyor ve Release yukleniyor..." -ForegroundColor Cyan
$zipFile = Join-Path $rootDir "Releases\PureAccessory_Update_v$Version.zip"
& powershell -ExecutionPolicy Bypass -File (Join-Path $rootDir "Publish_To_GitHub.ps1") -Version $Version -ZipPath $zipFile -ChangeLog $Message
if ($LASTEXITCODE -ne 0) {
    Write-Host "GitHub yüklemesi başarısız oldu!" -ForegroundColor Red
    exit 1
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "   TEBRIKLER! v$Version HEM VISUAL STUDIOYA HEM GITHUBA YUKLENDI!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
