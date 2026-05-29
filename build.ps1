param(
  [string]$DataPath,
  [switch]$UsePreviewData,
  [string]$ReleaseName
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
$Source = Join-Path $Root "src\Program.cs"
$Bin = Join-Path $Root "bin"
$DataDir = Join-Path $Bin "data"
$Out = Join-Path $Bin "PodexDesktop.exe"
$DefaultData = Join-Path $Root "data\pokemon.json"
$PreviewData = Join-Path $Root "artifacts\pokemon-catalog-preview.json"

if (-not $DataPath) {
  if ($UsePreviewData) {
    $DataPath = $PreviewData
  } else {
    $DataPath = $DefaultData
  }
}

if (-not $ReleaseName) {
  if ($UsePreviewData) {
    $ReleaseName = "PodexDesktop-catalog-preview"
  } else {
    $ReleaseName = "PodexDesktop"
  }
}

$JsonSource = $DataPath
$Release = Join-Path $Root ("release\" + $ReleaseName)
$ReleaseData = Join-Path $Release "data"
$ReleaseOut = Join-Path $Release "PodexDesktop.exe"
$AssetsImages = Join-Path $Root "assets\images"
$BinImages = Join-Path $Bin "images"
$ReleaseImages = Join-Path $Release "images"
$JsonOut = Join-Path $DataDir "pokemon.json"

if (-not (Test-Path $Csc)) {
  throw "C# compiler not found: $Csc"
}

if (-not (Test-Path $JsonSource)) {
  throw "Data file not found: $JsonSource."
}

New-Item -ItemType Directory -Force -Path $Bin | Out-Null
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseData | Out-Null
New-Item -ItemType Directory -Force -Path $BinImages | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseImages | Out-Null

& $Csc `
  /nologo `
  /target:winexe `
  /platform:x86 `
  /optimize+ `
  /out:$Out `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Drawing.dll `
  /reference:System.Web.Extensions.dll `
  /reference:System.Windows.Forms.dll `
  $Source

if ($LASTEXITCODE -ne 0) {
  throw "C# compilation failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $JsonSource -Destination $JsonOut -Force
Copy-Item -LiteralPath $Out -Destination $ReleaseOut -Force
Copy-Item -LiteralPath $JsonSource -Destination (Join-Path $ReleaseData "pokemon.json") -Force
if (Test-Path $AssetsImages) {
  Copy-Item -Path (Join-Path $AssetsImages "*") -Destination $BinImages -Recurse -Force
  Copy-Item -Path (Join-Path $AssetsImages "*") -Destination $ReleaseImages -Recurse -Force
}

@(
  "Podex Desktop",
  "",
  "Run PodexDesktop.exe directly.",
  "",
  "Data source:",
  "- $JsonSource",
  "",
  "Runtime requirements:",
  "- Windows",
  "- .NET Framework 4.x (same class of runtime requirement as the legacy app; Windows 10/11 usually includes 4.8)",
  "",
  "No Node, browser, WebView, database software, or development tools are required.",
  "The images folder contains migrated legacy sprites and item icons.",
  "Keep data\pokemon.json in this folder structure next to the executable."
) | Set-Content -Encoding UTF8 -Path (Join-Path $Release "README.txt")

Write-Output "Built $Out"
Write-Output "Copied data to $JsonOut"
Write-Output "Portable release: $Release"
