[CmdletBinding()]
param()

$conn = New-Object System.Data.SqlClient.SqlConnection("Server=localhost;Database=ERPSystem;Integrated Security=True;")
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME"
$r = $cmd.ExecuteReader()
$tables = @()
while ($r.Read()) {
    $tables += $r[0]
}
$r.Close()

Write-Host "================ MEVCUT VERITABANI TABLOLARI ================" -ForegroundColor Green
foreach ($t in $tables) {
    $cmd.CommandText = "SELECT COUNT(*) FROM [$t]"
    $count = $cmd.ExecuteScalar()
    Write-Host ("{0,-30} : {1,6} kayit" -f $t, $count) -ForegroundColor Cyan
}
$conn.Close()
