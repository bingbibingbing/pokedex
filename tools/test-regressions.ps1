param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
$Bin = Join-Path $Root "bin"
$Out = Join-Path $Bin "PodexRegressionTests.exe"

$Sources = @(
  (Join-Path $Root "tools\tests\RegressionTests.cs"),
  (Join-Path $Root "tools\ImportData.cs"),
  (Join-Path $Root "src\Program.cs")
)

if (-not (Test-Path $Csc)) {
  throw "C# compiler not found: $Csc"
}

foreach ($source in $Sources) {
  if (-not (Test-Path $source)) {
    throw "Required source not found: $source"
  }
}

New-Item -ItemType Directory -Force -Path $Bin | Out-Null

& $Csc `
  /nologo `
  /target:exe `
  /platform:x86 `
  /optimize+ `
  /out:$Out `
  /main:PodexRegressionTests.RegressionTests `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  /reference:Microsoft.VisualBasic.dll `
  /reference:System.Web.Extensions.dll `
  $Sources

if ($LASTEXITCODE -ne 0) {
  throw "C# compilation failed with exit code $LASTEXITCODE"
}

& $Out --console
exit $LASTEXITCODE
