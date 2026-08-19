param(
    [string]$OutputDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

function New-RoundedRectPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = [Math]::Max(1.0, $Radius * 2.0)

    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Fill-RoundedRect {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Brush]$Brush,
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-RoundedRectPath $X $Y $Width $Height $Radius
    try {
        $Graphics.FillPath($Brush, $path)
    }
    finally {
        $path.Dispose()
    }
}

function Draw-ProductIconCore {
    param(
        [System.Drawing.Graphics]$Graphics,
        [float]$Size
    )

    $black = [System.Drawing.Brushes]::Black

    # Speaker body: deliberately flattened and centered over the keyboard.
    Fill-RoundedRect $Graphics $black (0.305*$Size) (0.230*$Size) (0.125*$Size) (0.150*$Size) (0.024*$Size)

    $speakerCone = New-Object System.Drawing.Drawing2D.GraphicsPath
    try {
        $speakerCone.AddPolygon([System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF (0.415*$Size), (0.230*$Size)),
            (New-Object System.Drawing.PointF (0.535*$Size), (0.150*$Size)),
            (New-Object System.Drawing.PointF (0.560*$Size), (0.165*$Size)),
            (New-Object System.Drawing.PointF (0.560*$Size), (0.445*$Size)),
            (New-Object System.Drawing.PointF (0.535*$Size), (0.460*$Size)),
            (New-Object System.Drawing.PointF (0.415*$Size), (0.380*$Size))
        ))
        $Graphics.FillPath($black, $speakerCone)
    }
    finally {
        $speakerCone.Dispose()
    }

    # Rounded sound-wave strokes.
    $innerPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), (0.036*$Size)
    $outerPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), (0.040*$Size)
    try {
        $innerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $innerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $outerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $outerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

        $Graphics.DrawArc($innerPen, (0.565*$Size), (0.220*$Size), (0.120*$Size), (0.205*$Size), -58, 116)
        $Graphics.DrawArc($outerPen, (0.605*$Size), (0.155*$Size), (0.180*$Size), (0.330*$Size), -57, 114)
    }
    finally {
        $innerPen.Dispose()
        $outerPen.Dispose()
    }

    # Keyboard chassis.
    Fill-RoundedRect $Graphics $black (0.130*$Size) (0.500*$Size) (0.740*$Size) (0.330*$Size) (0.055*$Size)

    # Punch transparent key wells into the black keyboard body.
    $Graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $clear = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)
    try {
        $row1X = @(0.190, 0.290, 0.380, 0.470, 0.560, 0.650, 0.740)
        foreach ($x in $row1X) {
            Fill-RoundedRect $Graphics $clear ($x*$Size) (0.545*$Size) (0.072*$Size) (0.063*$Size) (0.011*$Size)
        }

        $row2 = @(
            @(0.190, 0.108),
            @(0.320, 0.066),
            @(0.420, 0.066),
            @(0.510, 0.066),
            @(0.600, 0.066),
            @(0.700, 0.108)
        )
        foreach ($key in $row2) {
            Fill-RoundedRect $Graphics $clear ($key[0]*$Size) (0.635*$Size) ($key[1]*$Size) (0.064*$Size) (0.011*$Size)
        }

        Fill-RoundedRect $Graphics $clear (0.190*$Size) (0.725*$Size) (0.085*$Size) (0.060*$Size) (0.011*$Size)
        Fill-RoundedRect $Graphics $clear (0.305*$Size) (0.725*$Size) (0.385*$Size) (0.060*$Size) (0.011*$Size)
        Fill-RoundedRect $Graphics $clear (0.720*$Size) (0.725*$Size) (0.085*$Size) (0.060*$Size) (0.011*$Size)
    }
    finally {
        $clear.Dispose()
        $Graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    }
}

function New-IconBitmap {
    param([int]$Size)

    # Supersample to preserve clean curves even for 16/24 px target-size assets.
    $workSize = [Math]::Max(256, $Size * 4)
    $work = New-Object System.Drawing.Bitmap $workSize, $workSize, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($work)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        Draw-ProductIconCore $g $workSize
    }
    finally {
        $g.Dispose()
    }

    if ($workSize -eq $Size) {
        return $work
    }

    $result = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rg = [System.Drawing.Graphics]::FromImage($result)
    try {
        $rg.Clear([System.Drawing.Color]::Transparent)
        $rg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $rg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $rg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $rg.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $rg.DrawImage($work, 0, 0, $Size, $Size)
    }
    finally {
        $rg.Dispose()
        $work.Dispose()
    }

    return $result
}

function Save-Icon {
    param([string]$Name, [int]$Size)
    $bitmap = New-IconBitmap $Size
    try {
        $bitmap.Save((Join-Path $OutputDir $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Save-Splash {
    param([string]$Name, [int]$Width, [int]$Height)

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $iconSize = [Math]::Round($Height * 0.70)
        $icon = New-IconBitmap $iconSize
        try {
            $x = [Math]::Round(($Width - $iconSize) / 2.0)
            $y = [Math]::Round(($Height - $iconSize) / 2.0)
            $g.DrawImage($icon, $x, $y, $iconSize, $iconSize)
        }
        finally {
            $icon.Dispose()
        }

        $bitmap.Save((Join-Path $OutputDir $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $g.Dispose()
        $bitmap.Dispose()
    }
}

# High-resolution master retained in the package/source tree for future regeneration.
Save-Icon 'ProductIcon.png' 1024

# Scale 100 assets referenced by Package.appxmanifest.
Save-Icon 'Square44x44Logo.png' 44
Save-Icon 'Square150x150Logo.png' 150
Save-Icon 'StoreLogo.png' 50
Save-Splash 'SplashScreen.png' 620 300

# Surface-class displays commonly use 200%; 400% keeps the same asset crisp at extreme DPI.
Save-Icon 'Square44x44Logo.scale-200.png' 88
Save-Icon 'Square150x150Logo.scale-200.png' 300
Save-Icon 'StoreLogo.scale-200.png' 100
Save-Splash 'SplashScreen.scale-200.png' 1240 600

Save-Icon 'Square44x44Logo.scale-400.png' 176
Save-Icon 'Square150x150Logo.scale-400.png' 600
Save-Icon 'StoreLogo.scale-400.png' 200
Save-Splash 'SplashScreen.scale-400.png' 2480 1200

# Transparent/unplated target-size assets improve taskbar and app-list rendering.
foreach ($targetSize in @(16, 24, 32, 48, 256)) {
    Save-Icon ("Square44x44Logo.targetsize-{0}.png" -f $targetSize) $targetSize
    Save-Icon ("Square44x44Logo.targetsize-{0}_altform-unplated.png" -f $targetSize) $targetSize
}

Write-Host "Generated transparent Touch Keyboard Audio assets in $OutputDir"
