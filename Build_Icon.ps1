Add-Type -AssemblyName System.Drawing
$pngPath = "c:\Users\krale\Desktop\ERPStarter\ERP.Desktop\Assets\AppLogo.png"
$icoPath = "c:\Users\krale\Desktop\ERPStarter\ERP.Desktop\Assets\AppLogo.ico"

$img = [System.Drawing.Image]::FromFile($pngPath)
$size = 256
$thumb = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($thumb)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.DrawImage($img, 0, 0, $size, $size)
$g.Dispose()

$hIcon = $thumb.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)
$fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$icon.Save($fs)
$fs.Close()
$thumb.Dispose()
$img.Dispose()

Write-Host "AppLogo.ico olusturuldu! Boyut: $((Get-Item $icoPath).Length) bytes"
