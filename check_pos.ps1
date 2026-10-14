$file = "ERP.Desktop\bin\Debug\net9.0-windows\Data\pos_transactions.json"
if (Test-Path $file) {
    $content = Get-Content $file -Raw | ConvertFrom-Json
    Write-Output "Total POS count: $($content.Count)"
    $latest = $content | Select-Object -First 5
    $latest | Format-Table Id, SaleId, SaleDate, GrossAmount, Status
} else {
    Write-Output "pos_transactions.json bulunamadi."
}

$salesFile = "ERP.Desktop\bin\Debug\net9.0-windows\Data\sales.json"
if (Test-Path $salesFile) {
    $sales = Get-Content $salesFile -Raw | ConvertFrom-Json
    Write-Output "Total Sales count: $($sales.Count)"
    $sales | Select-Object -First 5 | Format-Table Id, SaleDate, PaymentType, GrandTotal, CardAmount, CashAmount
}
