# Images de l'installeur (installer\images\*.png), dessinées avec WPF aux couleurs de src\OptiGame.App\Themes\Theme.xaml et
# avec le logo de src\OptiGame.App\Assets\OptiGame.svg (mêmes tracés que Logo.* de Theme.xaml). Les PNG sont versionnés :
# relancer ce script seulement si le logo, les couleurs ou les textes changent.
#   powershell -ExecutionPolicy Bypass -File scripts\installer-images.ps1
# Tailles = zones d'image d'Inno Setup 6.7 en style « modern », de 100 % à 250 % d'échelle d'affichage (documentation de
# WizardImageFile / WizardSmallImageFile) : l'installeur choisit lui-même la plus proche.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'installer\images'
New-Item -ItemType Directory -Force $out | Out-Null

function Color([string] $hex, [byte] $alpha = 255) {
    $c = [System.Windows.Media.ColorConverter]::ConvertFromString($hex)
    [System.Windows.Media.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}
function Brush([string] $hex, [byte] $alpha = 255) {
    $b = New-Object System.Windows.Media.SolidColorBrush (Color $hex $alpha)
    $b.Freeze(); $b
}

# Couleurs de Theme.xaml.
$window = '#0E1014'; $sidebar = '#12151B'; $accent = '#2FD27A'; $onAccent = '#06140C'
$text = '#EDEFF3'; $textSecondary = '#A2A9B4'

$dial = [System.Windows.Media.Geometry]::Parse('M22.5,48.5 A19,19 0 1 1 41.5,48.5')
$play = [System.Windows.Media.Geometry]::Parse('M27,23 L27,41 L42,32 Z')

# Logo (case de 64 unités) posé à (x, y) en taille $size.
function Draw-Logo($dc, [double] $x, [double] $y, [double] $size) {
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform $x, $y))
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 64), ($size / 64)))
    $dc.DrawRoundedRectangle((Brush $accent), $null, (New-Object System.Windows.Rect 0, 0, 64, 64), 14, 14)
    $dialPen = New-Object System.Windows.Media.Pen (Brush $onAccent), 6
    $dialPen.StartLineCap = 'Round'; $dialPen.EndLineCap = 'Round'
    $dc.DrawGeometry($null, $dialPen, $dial)
    $playPen = New-Object System.Windows.Media.Pen (Brush $onAccent), 3
    $playPen.LineJoin = 'Round'
    $dc.DrawGeometry((Brush $onAccent), $playPen, $play)
    $dc.Pop(); $dc.Pop()
}

function Text([string] $s, [double] $size, [string] $hex, [string] $weight = 'Normal') {
    $typeface = New-Object System.Windows.Media.Typeface (
        (New-Object System.Windows.Media.FontFamily 'Segoe UI Variable Display, Segoe UI'),
        [System.Windows.FontStyles]::Normal,
        [System.Windows.FontWeight]::FromOpenTypeWeight($(if ($weight -eq 'SemiBold') { 600 } else { 400 })),
        [System.Windows.FontStretches]::Normal)
    $ft = New-Object System.Windows.Media.FormattedText ($s, [Globalization.CultureInfo]::GetCultureInfo('fr-FR'),
        [System.Windows.FlowDirection]::LeftToRight, $typeface, $size, (Brush $hex), 1.0)
    $ft.TextAlignment = 'Center'
    $ft
}

# Dessin en unités logiques ($w x $h), rendu à $pixelW x $pixelH (même proportion), enregistré en PNG.
function Save-Png([string] $name, [double] $w, [double] $h, [int] $pixelW, [int] $pixelH, [scriptblock] $draw) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($pixelW / $w), ($pixelH / $h)))
    & $draw $dc
    $dc.Pop()
    $dc.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $pixelW, $pixelH, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $path = Join-Path $out $name
    $stream = [IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Write-Host "  $name ($pixelW x $pixelH)"
}

# Panneau des pages d'accueil et de fin : fond de la fenêtre d'OptiGame, halo vert, logo, nom, promesse.
$wizardSizes = @(@(202, 386), @(269, 515), @(336, 643), @(403, 772), @(430, 824), @(498, 953), @(534, 1022))
$panel = {
    param($dc)
    $W = 202; $H = 386
    $bg = New-Object System.Windows.Media.LinearGradientBrush (Color $sidebar), (Color $window), 90
    $dc.DrawRectangle($bg, $null, (New-Object System.Windows.Rect 0, 0, $W, $H))

    $glow = New-Object System.Windows.Media.RadialGradientBrush
    $glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (Color $accent 70), 0))
    $glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (Color $accent 0), 1))
    $dc.DrawEllipse($glow, $null, (New-Object System.Windows.Point ($W / 2), 132), 120, 120)

    # Cadran géant du logo, en filigrane, débordant du bas du panneau.
    $ghost = New-Object System.Windows.Media.Pen (Brush $accent 22), 2.2
    $ghost.StartLineCap = 'Round'; $ghost.EndLineCap = 'Round'
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform (($W / 2) - 32 * 5.2), 228))
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform 5.2, 5.2))
    $thin = $ghost.Clone(); $thin.Thickness = 2.2 / 5.2
    $dc.DrawGeometry($null, $thin, $dial)
    $dc.Pop(); $dc.Pop()

    Draw-Logo $dc (($W - 76) / 2) 94 76
    $dc.DrawText((Text 'OptiGame' 26 $text 'SemiBold'), (New-Object System.Windows.Point ($W / 2), 186))
    $dc.DrawText((Text "Plus de FPS,`nmoins de saccades" 13 $textSecondary), (New-Object System.Windows.Point ($W / 2), 224))
}
foreach ($s in $wizardSizes) { Save-Png "wizard-$($s[0]).png" 202 386 $s[0] $s[1] $panel }

# Petite image en haut à droite des autres pages : le logo seul, fond transparent.
$smallSizes = @(58, 77, 97, 116, 124, 143, 159)
$small = {
    param($dc)
    Draw-Logo $dc 7 7 44
}
foreach ($s in $smallSizes) { Save-Png "small-$s.png" 58 58 $s $s $small }
