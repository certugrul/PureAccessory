[CmdletBinding()]
param()

$j = Get-Content 'ERP.Desktop\bin\Debug\net10.0-windows\Data\financial_reminders.json' -Raw | ConvertFrom-Json

$conn = New-Object System.Data.SqlClient.SqlConnection("Server=localhost;Database=ERPSystem;Integrated Security=True;")
$conn.Open()

$sql = @"
IF NOT EXISTS (SELECT 1 FROM FinancialReminders WHERE Id = @id)
BEGIN
    SET IDENTITY_INSERT FinancialReminders ON;
    INSERT INTO FinancialReminders (Id, Title, [Type], Category, ReminderType, GeneralExpenseId, DebtId, Amount, MinPayment, AccruedInterest, TaxAmount, PrincipalPaid, CurrentTotalDebt, InterestStartDate, DayOfMonth, DueDate, IsRecurringMonthly, Status, IsPaid, PaidDate, PaidFrom, BankAccountId, InvoiceNo, RemainingDebtAfter, Description, IsDeleted)
    VALUES (@id, @t, @rtype, @c, @rtype, @geid, @did, @amt, @min, @aint, @tax, @ppaid, @ctot, @isdate, @dom, @ddate, @rec, @stat, @ispaid, @pdate, @pfrom, @baid, @invno, @rem, @desc, @del);
    SET IDENTITY_INSERT FinancialReminders OFF;
END
"@

$inserted = 0
foreach ($r in $j) {
    $t = if ($r.Title) { [string]$r.Title } else { "" }
    $rtype = if ($r.ReminderType) { [string]$r.ReminderType } else { "Genel Gider" }
    $c = if ($r.Category) { [string]$r.Category } else { "Genel" }
    $geid = if ($r.GeneralExpenseId) { [int]$r.GeneralExpenseId } else { [DBNull]::Value }
    $did = if ($r.DebtId) { [int]$r.DebtId } else { [DBNull]::Value }
    $amt = if ($r.Amount) { [decimal]$r.Amount } else { 0m }
    $min = if ($r.MinPayment) { [decimal]$r.MinPayment } else { [DBNull]::Value }
    $aint = if ($r.AccruedInterest) { [decimal]$r.AccruedInterest } else { [DBNull]::Value }
    $tax = if ($r.TaxAmount) { [decimal]$r.TaxAmount } else { [DBNull]::Value }
    $ppaid = if ($r.PrincipalPaid) { [decimal]$r.PrincipalPaid } else { [DBNull]::Value }
    $ctot = if ($r.CurrentTotalDebt) { [decimal]$r.CurrentTotalDebt } else { [DBNull]::Value }
    $isdate = if ($r.InterestStartDate) { [DateTime]$r.InterestStartDate } else { [DBNull]::Value }
    $dom = if ($r.DayOfMonth) { [int]$r.DayOfMonth } else { 15 }
    $ddate = if ($r.DueDate) { [DateTime]$r.DueDate } else { [DateTime]::Today }
    $rec = if ($null -ne $r.IsRecurringMonthly) { [bool]$r.IsRecurringMonthly } else { $true }
    $stat = if ($r.Status) { [string]$r.Status } else { "Bekliyor" }
    $ispaid = [bool]($r.Status -eq "Ödendi")
    $pdate = if ($r.PaidDate) { [DateTime]$r.PaidDate } else { [DBNull]::Value }
    $pfrom = if ($r.PaidFrom) { [string]$r.PaidFrom } else { [DBNull]::Value }
    $baid = if ($r.BankAccountId) { [int]$r.BankAccountId } else { [DBNull]::Value }
    $invno = if ($r.InvoiceNo) { [string]$r.InvoiceNo } else { [DBNull]::Value }
    $rem = if ($r.RemainingDebtAfter) { [decimal]$r.RemainingDebtAfter } else { [DBNull]::Value }
    $desc = if ($r.Description) { [string]$r.Description } else { [DBNull]::Value }
    $del = if ($null -ne $r.IsDeleted) { [bool]$r.IsDeleted } else { $false }

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql

    $cmd.Parameters.AddWithValue("@id", [long]$r.Id) | Out-Null
    $cmd.Parameters.AddWithValue("@t", $t) | Out-Null
    $cmd.Parameters.AddWithValue("@rtype", $rtype) | Out-Null
    $cmd.Parameters.AddWithValue("@c", $c) | Out-Null
    $cmd.Parameters.AddWithValue("@geid", $geid) | Out-Null
    $cmd.Parameters.AddWithValue("@did", $did) | Out-Null
    $cmd.Parameters.AddWithValue("@amt", $amt) | Out-Null
    $cmd.Parameters.AddWithValue("@min", $min) | Out-Null
    $cmd.Parameters.AddWithValue("@aint", $aint) | Out-Null
    $cmd.Parameters.AddWithValue("@tax", $tax) | Out-Null
    $cmd.Parameters.AddWithValue("@ppaid", $ppaid) | Out-Null
    $cmd.Parameters.AddWithValue("@ctot", $ctot) | Out-Null
    $cmd.Parameters.AddWithValue("@isdate", $isdate) | Out-Null
    $cmd.Parameters.AddWithValue("@dom", $dom) | Out-Null
    $cmd.Parameters.AddWithValue("@ddate", $ddate) | Out-Null
    $cmd.Parameters.AddWithValue("@rec", $rec) | Out-Null
    $cmd.Parameters.AddWithValue("@stat", $stat) | Out-Null
    $cmd.Parameters.AddWithValue("@ispaid", $ispaid) | Out-Null
    $cmd.Parameters.AddWithValue("@pdate", $pdate) | Out-Null
    $cmd.Parameters.AddWithValue("@pfrom", $pfrom) | Out-Null
    $cmd.Parameters.AddWithValue("@baid", $baid) | Out-Null
    $cmd.Parameters.AddWithValue("@invno", $invno) | Out-Null
    $cmd.Parameters.AddWithValue("@rem", $rem) | Out-Null
    $cmd.Parameters.AddWithValue("@desc", $desc) | Out-Null
    $cmd.Parameters.AddWithValue("@del", $del) | Out-Null

    try {
        $n = $cmd.ExecuteNonQuery()
        if ($n -gt 0) { $inserted++ }
    } catch {
        Write-Host "Kayit $($r.Id) Hatasi: $($_.Exception.Message)" -ForegroundColor Red
    }
}

$conn.Close()
Write-Host "Basariyla $inserted adet finansal hatirlatici aktarildi!" -ForegroundColor Green
