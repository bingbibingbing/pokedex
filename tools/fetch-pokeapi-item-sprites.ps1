param(
  [int]$From = 960,
  [int]$To = 2232,
  [string]$ItemsCsvPath,
  [string]$OutDir,
  [switch]$Force,
  [switch]$SkipUpstreamIndex,
  [int]$DelayMs = 25
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $ItemsCsvPath) {
  $ItemsCsvPath = Join-Path $Root "tools\import-data\source-cache\pokeapi-csv\items.csv"
}
if (-not $OutDir) {
  $OutDir = Join-Path $Root "assets\images\items"
}

if (-not (Test-Path -LiteralPath $ItemsCsvPath)) {
  throw "items.csv not found: $ItemsCsvPath"
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

function Read-CsvRows {
  param([string]$Path)
  Import-Csv -LiteralPath $Path
}

$itemsById = @{}
foreach ($row in Read-CsvRows -Path $ItemsCsvPath) {
  $id = [int]$row.id
  if ($id -ge $From -and $id -le $To) {
    $itemsById[$id] = $row.identifier
  }
}

$availableSprites = $null
if (-not $SkipUpstreamIndex) {
  $availableSprites = New-Object "System.Collections.Generic.HashSet[string]"
  try {
    $treeUrl = "https://api.github.com/repos/PokeAPI/sprites/git/trees/master?recursive=1"
    $tree = Invoke-RestMethod -Uri $treeUrl -UseBasicParsing -TimeoutSec 60
    foreach ($entry in $tree.tree) {
      $path = [string]$entry.path
      if ($path.StartsWith("sprites/items/", [System.StringComparison]::OrdinalIgnoreCase) -and $path.EndsWith(".png", [System.StringComparison]::OrdinalIgnoreCase)) {
        [void]$availableSprites.Add([System.IO.Path]::GetFileName($path))
      }
    }
  } catch {
    $htmlUrl = "https://github.com/PokeAPI/sprites/tree/master/sprites/items"
    $html = (Invoke-WebRequest -Uri $htmlUrl -UseBasicParsing -TimeoutSec 60).Content
    foreach ($match in [System.Text.RegularExpressions.Regex]::Matches($html, '"name":"([^"]+\.png)"')) {
      [void]$availableSprites.Add($match.Groups[1].Value)
    }
  }
}

$downloaded = 0
$skipped = 0
$missing = 0
$failed = 0

foreach ($id in ($itemsById.Keys | Sort-Object)) {
  $identifier = $itemsById[$id]
  $smallPath = Join-Path $SmallDir ($id.ToString() + ".png")
  $bigPath = Join-Path $BigDir ($id.ToString() + ".png")
  if (-not $Force -and (Test-Path -LiteralPath $smallPath) -and (Test-Path -LiteralPath $bigPath)) {
    $skipped++
    continue
  }

  $url = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/items/$identifier.png"
  if ($availableSprites -ne $null -and -not $availableSprites.Contains("$identifier.png")) {
    $missing++
    continue
  }

  try {
    $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30
    $stream = New-Object System.IO.MemoryStream @(,$response.Content)
    try {
      $source = [System.Drawing.Image]::FromStream($stream)
      try {
        Save-CanvasImage -Source $source -Path $bigPath -CanvasWidth 80 -CanvasHeight 80
        Save-CanvasImage -Source $source -Path $smallPath -CanvasWidth 32 -CanvasHeight 32 -ScaleUp
      } finally {
        $source.Dispose()
      }
    } finally {
      $stream.Dispose()
    }
    $downloaded++
  } catch [System.Net.WebException] {
    if ($_.Exception.Response -ne $null -and [int]$_.Exception.Response.StatusCode -eq 404) {
      $missing++
    } else {
      Write-Warning ("Failed to fetch item sprite {0} ({1}) from {2}: {3}" -f $id, $identifier, $url, $_.Exception.Message)
      $failed++
    }
  } catch {
    Write-Warning ("Failed to fetch item sprite {0} ({1}) from {2}: {3}" -f $id, $identifier, $url, $_.Exception.Message)
    $failed++
  }

  if ($DelayMs -gt 0) {
    Start-Sleep -Milliseconds $DelayMs
  }
}

Write-Output ("Item sprites downloaded: {0}" -f $downloaded)
Write-Output ("Item sprites skipped: {0}" -f $skipped)
Write-Output ("Item sprites missing upstream: {0}" -f $missing)
Write-Output ("Item sprites failed: {0}" -f $failed)
if ($failed -gt 0) {
  exit 1
}
