Add-Type -AssemblyName System.Drawing

$pngPath = "c:\Users\krale\Desktop\ERPStarter\ERP.Desktop\Assets\AppLogo.png"
$icoPath = "c:\Users\krale\Desktop\ERPStarter\ERP.Desktop\Assets\AppLogo.ico"
$previewPath = "c:\Users\krale\Desktop\ERPStarter\ERP.Desktop\Assets\preview_test.png"

# Remove preview_test.png if exists
if (Test-Path $previewPath) { Remove-Item $previewPath -Force }

# Read original AppLogo.png into memory
$bytes = [System.IO.File]::ReadAllBytes($pngPath)
$ms = New-Object System.IO.MemoryStream(,$bytes)
$orig = New-Object System.Drawing.Bitmap($ms)

$bgColor = [System.Drawing.ColorTranslator]::FromHtml("#948428")

# Create 512x512 cleanly padded AppLogo.png using WrapMode.TileFlipXY so there is zero edge line
$size = 512
$padded = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($padded)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear($bgColor)

$ia = New-Object System.Drawing.Imaging.ImageAttributes
$ia.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)

# Draw the 500x500 graphic at 340x340 centered optically at (86, 94) so P and ure are dead-center in the circle
$destRect = New-Object System.Drawing.Rectangle(86, 94, 340, 340)
$g.DrawImage($orig, $destRect, 0, 0, $orig.Width, $orig.Height, [System.Drawing.GraphicsUnit]::Pixel, $ia)

$ia.Dispose()
$g.Dispose()
$orig.Dispose()
$ms.Dispose()

# Save updated AppLogo.png
$padded.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Also create circular/rounded AppLogo.ico (256x256)
$icoSize = 256
$icoBmp = New-Object System.Drawing.Bitmap($icoSize, $icoSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gi = [System.Drawing.Graphics]::FromImage($icoBmp)
$gi.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$gi.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$gi.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$gi.Clear([System.Drawing.Color]::Transparent)

$clipPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$clipPath.AddEllipse(4, 4, 248, 248)
$gi.SetClip($clipPath)
$gi.DrawImage($padded, 4, 4, 248, 248)
$gi.ResetClip()

$pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml("#EAB308"), 6)
$gi.DrawEllipse($pen, 4, 4, 248, 248)
$pen.Dispose()
$clipPath.Dispose()
$gi.Dispose()

$hIcon = $icoBmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)
$fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$icon.Save($fs)
$fs.Close()

$icoBmp.Dispose()
$padded.Dispose()

Write-Host "AppLogo.png ve AppLogo.ico basariyla guncellendi!"
