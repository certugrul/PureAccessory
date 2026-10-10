[CmdletBinding()]
param()

$tempBackupDir = "C:\SQLBackup"
if (-not (Test-Path $tempBackupDir)) {
    New-Item -ItemType Directory -Path $tempBackupDir -Force | Out-Null
}
& icacls "$tempBackupDir" /grant "Everyone:(OI)(CI)F" /q | Out-Null

$tempBackupFile = Join-Path $tempBackupDir "ERPSystem.bak"
$finalBackupDir = "C:\Users\krale\Desktop\ERPStarter\Releases"
$finalBackupFile = Join-Path $finalBackupDir "ERPSystem.bak"

$instances = @("localhost", "(local)", ".\SQLEXPRESS")
$success = $false

foreach ($inst in $instances) {
    try {
        $connStr = "Server=$inst;Database=master;Integrated Security=True;"
        $conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
        $conn.Open()
        
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "BACKUP DATABASE [ERPSystem] TO DISK = N'$tempBackupFile' WITH FORMAT, INIT, NAME = N'ERPSystem-Full Backup', SKIP, NOREWIND, NOUNLOAD, STATS = 10"
        $cmd.ExecuteNonQuery() | Out-Null
        $conn.Close()
        
        # Hedef klasöre de kopyala
        Copy-Item -Path $tempBackupFile -Destination $finalBackupFile -Force
        
        Write-Host "BASARILI: Yedek alindi!" -ForegroundColor Green
        Write-Host "1. $tempBackupFile" -ForegroundColor Cyan
        Write-Host "2. $finalBackupFile" -ForegroundColor Cyan
        $success = $true
        break
    } catch {
        Write-Host "Denenen ($inst): $($_.Exception.Message)" -ForegroundColor DarkGray
    }
}

if (-not $success) {
    Write-Host "HATA: SQL Server yedeği alinamadi." -ForegroundColor Red
}
