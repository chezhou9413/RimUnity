param([string]$Output = (Join-Path $PSScriptRoot '../../About/ModIcon.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

#透明底白色 Unity 徽记，留出边距以适应模组列表的小尺寸显示。
$coordinates = @(40,0, 40,17.5, 23.5,27.2, 44.2,39.6,
    64.7,27.2, 47.5,17.5, 47.5,0, 88.5,23, 88.5,69.2,
    72.8,60.4, 72.8,41.4, 51.3,53.3, 51.3,76.4,
    68.5,66.8, 84.5,76.1, 44.2,99.5, 3.7,76.1,
    19.1,66.8, 36.2,76.4, 36.2,53.3, 15.1,41.4,
    15.1,60.4, 0,69.7, 0,23)
$points = [System.Drawing.PointF[]]::new($coordinates.Count / 2)
$scale = 864.0 / 99.5
for ($i = 0; $i -lt $points.Length; $i++) {
    $points[$i] = [System.Drawing.PointF]::new(
        (1024 - 88.5 * $scale) / 2 + $coordinates[$i * 2] * $scale,
        80 + $coordinates[$i * 2 + 1] * $scale)
}
$canvas = [System.Drawing.Bitmap]::new(1024, 1024)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
$icon = [System.Drawing.Bitmap]::new(256, 256)
$outputGraphics = [System.Drawing.Graphics]::FromImage($icon)
try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.FillPolygon([System.Drawing.Brushes]::White, $points)
    $outputGraphics.Clear([System.Drawing.Color]::Transparent)
    $outputGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $outputGraphics.DrawImage($canvas, 0, 0, 256, 256)
    $icon.Save([IO.Path]::GetFullPath($Output), [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $outputGraphics.Dispose()
    $icon.Dispose()
    $graphics.Dispose()
    $canvas.Dispose()
}
Write-Output "已生成模组图标：$Output"
