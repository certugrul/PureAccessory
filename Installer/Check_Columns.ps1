[CmdletBinding()]
param()

$c = New-Object System.Data.SqlClient.SqlConnection("Server=localhost;Database=ERPSystem;Integrated Security=True;")
$c.Open()
$cmd = $c.CreateCommand()
$cmd.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'FinancialReminders'"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host "$($r[0]) : $($r[1])"
}
$c.Close()
