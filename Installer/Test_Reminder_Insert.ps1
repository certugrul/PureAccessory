[CmdletBinding()]
param()

$j = Get-Content 'ERP.Desktop\bin\Debug\net10.0-windows\Data\financial_reminders.json' -Raw | ConvertFrom-Json
$r = $j[0]

$conn = New-Object System.Data.SqlClient.SqlConnection("Server=localhost;Database=ERPSystem;Integrated Security=True;")
$conn.Open()

$sql = "SET IDENTITY_INSERT FinancialReminders ON; INSERT INTO FinancialReminders (Id, Title, [Type], Category, ReminderType, GeneralExpenseId, DebtId, Amount, MinPayment, AccruedInterest, TaxAmount, PrincipalPaid, CurrentTotalDebt, InterestStartDate, DayOfMonth, DueDate, IsRecurringMonthly, Status, IsPaid, PaidDate, PaidFrom, BankAccountId, InvoiceNo, RemainingDebtAfter, Description, IsDeleted) VALUES (@id, @t, @rtype, @c, @rtype, @geid, @did, @amt, @min, @aint, @tax, @ppaid, @ctot, @isdate, @dom, @ddate, @rec, @stat, @ispaid, @pdate, @pfrom, @baid, @invno, @rem, @desc, @del); SET IDENTITY_INSERT FinancialReminders OFF;"

$cmd = $conn.CreateCommand()
$cmd.CommandText = $sql

$cmd.Parameters.AddWithValue("@id", [long]$r.Id) | Out-Null
$cmd.Parameters.AddWithValue("@t", [string]$r.Title) | Out-Null
$cmd.Parameters.AddWithValue("@rtype", [string]$r.ReminderType) | Out-Null
$cmd.Parameters.AddWithValue("@c", [string]$r.Category) | Out-Null
$cmd.Parameters.AddWithValue("@geid", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@did", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@amt", [decimal]$r.Amount) | Out-Null
$cmd.Parameters.AddWithValue("@min", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@aint", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@tax", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@ppaid", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@ctot", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@isdate", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@dom", [int]$r.DayOfMonth) | Out-Null
$cmd.Parameters.AddWithValue("@ddate", [DateTime]$r.DueDate) | Out-Null
$cmd.Parameters.AddWithValue("@rec", [bool]$r.IsRecurringMonthly) | Out-Null
$cmd.Parameters.AddWithValue("@stat", [string]$r.Status) | Out-Null
$cmd.Parameters.AddWithValue("@ispaid", [bool]($r.Status -eq "Ödendi")) | Out-Null
$cmd.Parameters.AddWithValue("@pdate", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@pfrom", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@baid", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@invno", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@rem", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@desc", [DBNull]::Value) | Out-Null
$cmd.Parameters.AddWithValue("@del", [bool]$r.IsDeleted) | Out-Null

try {
    $res = $cmd.ExecuteNonQuery()
    Write-Host "Success: $res" -ForegroundColor Green
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
$conn.Close()
