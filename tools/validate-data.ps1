param(
  [string]$DataPath,
  [string]$ImageRoot,
  [string]$ReportPath,
  [switch]$FailOnWarnings
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
$Source = Join-Path $Root "tools\ValidateData.cs"
$Bin = Join-Path $Root "bin"
$Out = Join-Path $Bin "PodexDataValidator.exe"

if (-not $DataPath) {
  $DataPath = Join-Path $Root "data\pokemon.json"
}

if (-not $ImageRoot) {
  $ImageRoot = Join-Path $Root "assets\images"
}

if (-not $ReportPath) {
  $ReportPath = Join-Path $Root "artifacts\data-validation-report.txt"
}

if (-not (Test-Path $Csc)) {
  throw "C# compiler not found: $Csc"
}

if (-not (Test-Path $Source)) {
  throw "Validator source not found: $Source"
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
  "--images", (Resolve-Path -LiteralPath $ImageRoot).Path,
  "--report", $ReportPath
)

if ($FailOnWarnings) {
  $argsList += "--fail-on-warnings"
}

& $Out @argsList
exit $LASTEXITCODE
