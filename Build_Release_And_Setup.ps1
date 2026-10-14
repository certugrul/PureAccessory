# =====================================================================
# Pure Accessory Otomasyon - Otomatik Sürüm Derleme ve Paketleyici
# =====================================================================
param(
    [string]$Version = "1.0.0",
    [string]$ChangeLog = "• Sistem güncellemeleri, borç/finans yönetimi ve yeni özellikler eklendi."
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$publishDir = Join-Path $rootDir "Release_Publish"
$releasesDir = Join-Path $rootDir "Releases"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "    PURE ACCESSORY OTOMASYON - SURUM DERLEME SISTEMI      " -ForegroundColor Green
Write-Host "    Hedef Sürüm: v$Version                                " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Klasörleri hazırla
Get-Process | Where-Object { 
    try { $_.Path -and $_.Path.StartsWith($publishDir, [System.StringComparison]::OrdinalIgnoreCase) } 
    catch { $false } 
} | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $publishDir) {
    Write-Host "Eski publish klasörü temizleniyor..." -ForegroundColor Gray
    try {
        Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
    } catch {}
}
if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
}

# 1.1 Visual Studio .csproj dosyalarındaki sürüm numarasını güncelle
Write-Host "Visual Studio proje dosyaları (ERP.Desktop ve ERP.Updater) v$Version olarak güncelleniyor..." -ForegroundColor Cyan
$desktopProj = Join-Path $rootDir "ERP.Desktop\ERP.Desktop.csproj"
$updaterProj = Join-Path $rootDir "ERP.Updater\ERP.Updater.csproj"

foreach ($projPath in @($desktopProj, $updaterProj)) {
    if (Test-Path $projPath) {
        $content = Get-Content $projPath -Raw
        $content = [System.Text.RegularExpressions.Regex]::Replace($content, "<Version>.*?</Version>", "<Version>$Version</Version>")
        $content = [System.Text.RegularExpressions.Regex]::Replace($content, "<AssemblyVersion>.*?</AssemblyVersion>", "<AssemblyVersion>$Version.0</AssemblyVersion>")
        $content = [System.Text.RegularExpressions.Regex]::Replace($content, "<FileVersion>.*?</FileVersion>", "<FileVersion>$Version.0</FileVersion>")
        [System.IO.File]::WriteAllText($projPath, $content, [System.Text.Encoding]::UTF8)
    }
}

# 2. ERP.Desktop'ı Self-Contained Olarak Derle
Write-Host "1. ERP.Desktop self-contained (win-x64) olarak derleniyor..." -ForegroundColor White
& dotnet publish $desktopProj -c Release -r win-x64 --self-contained true -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" -p:InformationalVersion="$Version" -o $publishDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERP.Desktop derleme hatası!" -ForegroundColor Red
    exit 1
}

# 3. ERP.Updater'ı Derle ve Tools Klasörüne Yerleştir
Write-Host "2. Updater.exe tek dosya olarak derleniyor..." -ForegroundColor White
$updaterProj = Join-Path $rootDir "ERP.Updater\ERP.Updater.csproj"
$toolsDir = Join-Path $publishDir "Tools"
if (-not (Test-Path $toolsDir)) {
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
}
& dotnet publish $updaterProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" -p:InformationalVersion="$Version" -o $toolsDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "Updater derleme hatası!" -ForegroundColor Red
    exit 1
}

# Gereksiz pdb dosyalarını temizle
Get-ChildItem -Path $publishDir -Filter "*.pdb" -Recurse | Remove-Item -Force

# 4. Uzaktan Güncelleme Paketi (update.zip) Oluştur
Write-Host "3. Uzaktan güncelleme paketi (ZIP) oluşturuluyor..." -ForegroundColor White
$updateZipPath = Join-Path $releasesDir "PureAccessory_Update_v$Version.zip"
if (Test-Path $updateZipPath) { Remove-Item -Force $updateZipPath }

# Data/settings.json ve users.json dosyalarını güncelleme paketinden hariç tut (kullanıcı verileri bozulmasın)
$tempZipFolder = Join-Path $rootDir "Temp_Zip_Folder"
if (Test-Path $tempZipFolder) { Remove-Item -Recurse -Force $tempZipFolder }
New-Item -ItemType Directory -Path $tempZipFolder -Force | Out-Null
Copy-Item -Path "$publishDir\*" -Destination $tempZipFolder -Recurse -Force
if (Test-Path "$tempZipFolder\Data") {
    Remove-Item -Recurse -Force "$tempZipFolder\Data"
}

Compress-Archive -Path "$tempZipFolder\*" -DestinationPath $updateZipPath -Force
Remove-Item -Recurse -Force $tempZipFolder
Write-Host "   -> Güncelleme Paketi: $updateZipPath" -ForegroundColor Green

# 5. version.json Dosyası Oluştur
Write-Host "4. version.json dosyası oluşturuluyor..." -ForegroundColor White
$todayStr = (Get-Date).ToString("dd.MM.yyyy")
$versionJsonObj = @{
    Version = $Version
    ReleaseDate = $todayStr
    DownloadUrl = "BURAYA_GUNCELLEME_ZIP_LINKINIZI_YAZIN"
    ChangeLog = $ChangeLog
    Mandatory = $false
}
$versionJsonPath = Join-Path $releasesDir "version.json"
$versionJsonObj | ConvertTo-Json -Depth 4 | Set-Content -Path $versionJsonPath -Encoding UTF8
Write-Host "   -> version.json: $versionJsonPath" -ForegroundColor Green

# 6. Inno Setup veya Standalone Kurulum Paketi Oluştur
Write-Host "5. Kurulum paketi (Setup) hazırlanıyor..." -ForegroundColor White
$isccCandidates = @(
    "ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$isccPath = $null
foreach ($cand in $isccCandidates) {
    if (Get-Command $cand -ErrorAction SilentlyContinue) { $isccPath = $cand; break }
    if (Test-Path $cand) { $isccPath = $cand; break }
}

if ($isccPath) {
    Write-Host "   Inno Setup bulundu ($isccPath). Setup.exe derleniyor..." -ForegroundColor Cyan
    $issFile = Join-Path $rootDir "Installer\Setup_Script.iss"
    & "$isccPath" "/DMyAppVersion=$Version" "$issFile"
    Write-Host "   -> PureAccessory_Setup_v$Version.exe başarıyla oluşturuldu!" -ForegroundColor Green
} else {
    Write-Host "   Inno Setup bulunamadı. Tek tıkla otomatik kurulabilir paket hazırlanıyor..." -ForegroundColor Yellow
    $setupFolder = Join-Path $releasesDir "PureAccessory_KurulumPaketi_v$Version"
    if (Test-Path $setupFolder) { Remove-Item -Recurse -Force $setupFolder }
    New-Item -ItemType Directory -Path $setupFolder -Force | Out-Null
    
    # AppFiles klasörüne yayın dosyalarını koy
    $appFilesTarget = Join-Path $setupFolder "AppFiles"
    New-Item -ItemType Directory -Path $appFilesTarget -Force | Out-Null
    Copy-Item -Path "$publishDir\*" -Destination $appFilesTarget -Recurse -Force
    
    # Kurulum betiklerini ekle
    Copy-Item -Path (Join-Path $rootDir "Installer\Install_PureAccessory.ps1") -Destination $setupFolder -Force
    Copy-Item -Path (Join-Path $rootDir "Installer\Kurulum_Yap.bat") -Destination $setupFolder -Force
    Copy-Item -Path (Join-Path $rootDir "Installer\SQL_Kontrol_Ve_Onar.ps1") -Destination $setupFolder -Force
    Copy-Item -Path (Join-Path $rootDir "Installer\SQL_Kontrol_Ve_Onar.bat") -Destination $setupFolder -Force
    Copy-Item -Path (Join-Path $rootDir "Installer\SQL_Kontrol_Ve_Onar.bat") -Destination $appFilesTarget -Force
    
    # Zip haline getir
    $setupZipPath = Join-Path $releasesDir "PureAccessory_KurulumPaketi_v$Version.zip"
    if (Test-Path $setupZipPath) { Remove-Item -Force $setupZipPath }
    Compress-Archive -Path "$setupFolder\*" -DestinationPath $setupZipPath -Force
    Remove-Item -Recurse -Force $setupFolder
    Write-Host "   -> Taşınabilir Kurulum Paketi: $setupZipPath" -ForegroundColor Green
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "               TÜM PAKETLER HAZIRLANDI!                   " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host ""
Write-Host "Çıktı Klasörü: $releasesDir" -ForegroundColor White
Write-Host "1. Yeni bilgisayara kurulum için:" -ForegroundColor Cyan
Write-Host "   - 'Releases' klasöründeki kurulum dosyasını hedef bilgisayara götürüp 'Kurulum_Yap.bat' veya Setup.exe'yi çalıştırın." -ForegroundColor Gray
Write-Host "2. Uzaktan güncelleme yapmak için:" -ForegroundColor Cyan
Write-Host "   - 'PureAccessory_Update_v$Version.zip' dosyasını internete (GitHub Releases, sunucu vb.) yükleyin." -ForegroundColor Gray
Write-Host "   - 'version.json' dosyasındaki DownloadUrl adresine bu linki yazıp sunucuya yükleyin." -ForegroundColor Gray
Write-Host "   - Müşteri bilgisayarındaki ERP açıldığında yeni sürümü otomatik indirip kuracaktır!" -ForegroundColor Gray
Write-Host ""
