param(
  [int]$From = 808,
  [int]$To = 1025,
  [string]$OutDir,
  [string]$UrlTemplate = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{0}.png",
  [switch]$Force,
  [int]$DelayMs = 100
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $OutDir) {
  $OutDir = Join-Path $Root "assets\images\pokemon"
}

$SmallDir = Join-Path $OutDir "small"
$BigDir = Join-Path $OutDir "big"
New-Item -ItemType Directory -Force -Path $SmallDir | Out-Null
New-Item -ItemType Directory -Force -Path $BigDir | Out-Null

Add-Type -AssemblyName System.Drawing

function Save-CanvasImage {
  param(
    [System.Drawing.Image]$Source,
    [string]$Path,
    [int]$CanvasWidth,
    [int]$CanvasHeight,
    [switch]$ScaleUp
  )

  $scale = [Math]::Min($CanvasWidth / [double]$Source.Width, $CanvasHeight / [double]$Source.Height)
  if (-not $ScaleUp -and $scale -gt 1.0) {
    $scale = 1.0
  }
  $drawWidth = [Math]::Max(1, [int][Math]::Round($Source.Width * $scale))
  $drawHeight = [Math]::Max(1, [int][Math]::Round($Source.Height * $scale))
  $x = [int][Math]::Floor(($CanvasWidth - $drawWidth) / 2.0)
  $y = [int][Math]::Floor(($CanvasHeight - $drawHeight) / 2.0)

  $bitmap = New-Object System.Drawing.Bitmap $CanvasWidth, $CanvasHeight, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  try {
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
      $graphics.Clear([System.Drawing.Color]::Transparent)
      $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
      $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
      $graphics.DrawImage($Source, $x, $y, $drawWidth, $drawHeight)
    } finally {
      $graphics.Dispose()
    }
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
  } finally {
    $bitmap.Dispose()
  }
}

$downloaded = 0
$skipped = 0
$failed = 0

for ($id = $From; $id -le $To; $id++) {
  $smallPath = Join-Path $SmallDir ($id.ToString() + ".png")
  $bigPath = Join-Path $BigDir ($id.ToString() + ".png")
  if (-not $Force -and (Test-Path -LiteralPath $smallPath) -and (Test-Path -LiteralPath $bigPath)) {
    $skipped++
    continue
  }

  $url = [string]::Format($UrlTemplate, $id)
  try {
    $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30
    $stream = New-Object System.IO.MemoryStream @(,$response.Content)
    try {
      $source = [System.Drawing.Image]::FromStream($stream)
      try {
        Save-CanvasImage -Source $source -Path $bigPath -CanvasWidth 100 -CanvasHeight 100
        Save-CanvasImage -Source $source -Path $smallPath -CanvasWidth 40 -CanvasHeight 32 -ScaleUp
      } finally {
        $source.Dispose()
      }
    } finally {
      $stream.Dispose()
    }
    $downloaded++
  } catch {
    Write-Warning ("Failed to fetch sprite {0} from {1}: {2}" -f $id, $url, $_.Exception.Message)
    $failed++
  }

  if ($DelayMs -gt 0) {
    Start-Sleep -Milliseconds $DelayMs
  }
}

Write-Output ("Pokemon sprites downloaded: {0}" -f $downloaded)
Write-Output ("Pokemon sprites skipped: {0}" -f $skipped)
Write-Output ("Pokemon sprites failed: {0}" -f $failed)
if ($failed -gt 0) {
  exit 1
}
