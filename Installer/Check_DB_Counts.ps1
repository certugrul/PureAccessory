[CmdletBinding()]
param()

$tables = @('Users', 'Products', 'Warehouses', 'Sales', 'CashSessions', 'CashTransactions', 'Closures', 'CariAccounts', 'BankAccounts', 'Invoices', 'FinancialDebts', 'FinancialReminders')
$connStr = "Server=localhost;Database=ERPSystem;Integrated Security=True;"
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()

foreach ($t in $tables) {
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "SELECT COUNT(*) FROM [$t]"
        $count = $cmd.ExecuteScalar()
        Write-Host "$t : $count kayit" -ForegroundColor Green
    } catch {
        Write-Host "$t : Hata ($($_.Exception.Message))" -ForegroundColor Red
    }
}
$conn.Close()
