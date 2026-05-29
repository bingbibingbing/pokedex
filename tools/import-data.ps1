param(
  [string]$DataPath,
  [string]$SourcePath,
  [string]$ReportPath,
  [string]$MapPath,
  [string]$PreviewDataPath,
  [switch]$RequireSource
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
$Source = Join-Path $Root "tools\ImportData.cs"
$Bin = Join-Path $Root "bin"
$Out = Join-Path $Bin "PodexDataImporter.exe"

if (-not $DataPath) {
  $DataPath = Join-Path $Root "data\pokemon.json"
}

if (-not $SourcePath) {
  $SourcePath = Join-Path $Root "tools\import-data\source-cache\pokeapi-csv"
}

if (-not $ReportPath) {
  $ReportPath = Join-Path $Root "artifacts\import-data-report.txt"
}

if (-not $MapPath) {
  $MapPath = Join-Path $Root "artifacts\import-id-map-preview.csv"
}

if (-not $PreviewDataPath) {
  $PreviewDataPath = Join-Path $Root "artifacts\pokemon-catalog-preview.json"
}

if (-not (Test-Path $Csc)) {
  throw "C# compiler not found: $Csc"
}

if (-not (Test-Path $Source)) {
  throw "Importer source not found: $Source"
}

New-Item -ItemType Directory -Force -Path $Bin | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null

& $Csc `
  /nologo `
  /target:exe `
  /platform:x86 `
  /optimize+ `
  /out:$Out `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Web.Extensions.dll `
  $Source

if ($LASTEXITCODE -ne 0) {
  throw "C# compilation failed with exit code $LASTEXITCODE"
}

$argsList = @(
  "--data", (Resolve-Path -LiteralPath $DataPath).Path,
  "--source", $SourcePath,
  "--report", $ReportPath,
  "--map", $MapPath,
  "--preview-data", $PreviewDataPath
)

if ($RequireSource) {
  $argsList += "--require-source"
}

& $Out @argsList
exit $LASTEXITCODE
