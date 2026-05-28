param(
  [string]$LegacyDir,
  [string]$OutDir
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$WorkspaceRoot = Split-Path -Parent $Root

if ([string]::IsNullOrWhiteSpace($LegacyDir)) {
  $LegacyCandidate = Get-ChildItem -Path $WorkspaceRoot -Directory |
    Where-Object { $_.Name -like "PokeDex_v1.2Build34*" } |
    Select-Object -First 1

  if ($null -eq $LegacyCandidate) {
    throw "Legacy directory not found under $WorkspaceRoot"
  }

  $LegacyDir = $LegacyCandidate.FullName
}

if ([string]::IsNullOrWhiteSpace($OutDir)) {
  $OutDir = Join-Path $Root "assets\images"
}

$ImageAssemblyPath = Join-Path $LegacyDir "System.Data.SQLite.EF6.dll"
if (-not (Test-Path $ImageAssemblyPath)) {
  throw "Legacy image assembly not found: $ImageAssemblyPath"
}

Add-Type -AssemblyName System.Drawing
$Assembly = [Reflection.Assembly]::LoadFile($ImageAssemblyPath)

function Export-ResourceImages {
  param(
    [string]$ResourceNamePart,
    [string]$KeyPrefix,
    [string]$TargetSubDir
  )

  $ResourceName = $Assembly.GetManifestResourceNames() |
    Where-Object { $_ -like "*$ResourceNamePart*" } |
    Select-Object -First 1

  if ([string]::IsNullOrWhiteSpace($ResourceName)) {
    throw "Resource not found: $ResourceNamePart"
  }

  $TargetDir = Join-Path $OutDir $TargetSubDir
  New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

  $Stream = $Assembly.GetManifestResourceStream($ResourceName)
  $Reader = New-Object System.Resources.ResourceReader($Stream)
  $Count = 0

  foreach ($Entry in $Reader) {
    $Key = [string]$Entry.Key
    if (-not $Key.StartsWith($KeyPrefix)) {
      continue
    }

    $IdText = $Key.Substring($KeyPrefix.Length)
    $Id = [int]$IdText
    $Path = Join-Path $TargetDir ("$Id.png")
    $Bitmap = $Entry.Value
    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $Count++
  }

  $Reader.Close()
  $Stream.Close()
  Write-Output "Exported $Count images to $TargetDir"
}

Export-ResourceImages "PM_Small" "PM_Small_" "pokemon\small"
Export-ResourceImages "PM_Big" "PM_Big_" "pokemon\big"
Export-ResourceImages "IT_Small" "IT_Small_" "items\small"
Export-ResourceImages "IT_Big" "IT_Big_" "items\big"

function Export-SelectedResourceImages {
  param(
    [string]$ResourceNamePart,
    [string]$TargetSubDir,
    [scriptblock]$GetName
  )

  $ResourceName = $Assembly.GetManifestResourceNames() |
    Where-Object { $_ -like "*$ResourceNamePart*" } |
    Select-Object -First 1

  if ([string]::IsNullOrWhiteSpace($ResourceName)) {
    throw "Resource not found: $ResourceNamePart"
  }

  $TargetDir = Join-Path $OutDir $TargetSubDir
  New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

  $Stream = $Assembly.GetManifestResourceStream($ResourceName)
  $Reader = New-Object System.Resources.ResourceReader($Stream)
  $Count = 0

  foreach ($Entry in $Reader) {
    $FileName = & $GetName ([string]$Entry.Key)
    if ([string]::IsNullOrWhiteSpace($FileName)) {
      continue
    }

    $Path = Join-Path $TargetDir $FileName
    $Bitmap = $Entry.Value
    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $Count++
  }

  $Reader.Close()
  $Stream.Close()
  Write-Output "Exported $Count images to $TargetDir"
}

Export-SelectedResourceImages "MV_Category" "moves\category" {
  param([string]$Key)
  if ($Key -match '^MV_Category_(\d+)$') { return "$([int]$Matches[1]).png" }
  return $null
}

Export-SelectedResourceImages "MV_Range" "moves\range" {
  param([string]$Key)
  if ($Key -match '^MV_Range_(\d+)$') { return "$([int]$Matches[1]).png" }
  return $null
}

Export-SelectedResourceImages "Type.resources" "types\zhCN" {
  param([string]$Key)
  if ($Key -match '^Type_(\d+)_(\d+)$' -and [int]$Matches[2] -eq 1) {
    return "$([int]$Matches[1]).png"
  }
  return $null
}
