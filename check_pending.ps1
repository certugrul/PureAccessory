$file = "ERP.Desktop\bin\Debug\net9.0-windows\Data\pos_transactions.json"
$content = Get-Content $file -Raw | ConvertFrom-Json
$pending = @($content | Where-Object { $_.Status -eq "Bekliyor" })
Write-Output "Bekleyen Toplam Sayisi: $($pending.Count)"
$pending | Format-Table Id, SaleId, SaleDate, GrossAmount, ExpectedDate, BankAccountName, Status
