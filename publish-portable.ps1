# Self-contained portable tray build (no install, no MSIX).
# Staging: artifacts/portable/
# Release: artifacts/release/v<Version>/BuildnBits.Usage-<Version>-portable-win-x64.zip + SHA256SUMS.txt

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$version = "1.2.0"
$props = Join-Path $root "Directory.Build.props"
if (Test-Path $props) {
    $m = Select-String -Path $props -Pattern "<Version>([^<]+)</Version>" | Select-Object -First 1
    if ($m) { $version = $m.Matches[0].Groups[1].Value }
}

$out = Join-Path $root "artifacts\portable"
$releaseDir = Join-Path $root "artifacts\release\v$version"
$zipName = "BuildnBits.Usage-$version-portable-win-x64.zip"
$zip = Join-Path $releaseDir $zipName

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out, $releaseDir | Out-Null

dotnet publish (Join-Path $root "src\BuildnBits.Usage.Tray\BuildnBits.Usage.Tray.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:SatelliteResourceLanguages=en `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $out

Get-ChildItem $out -Filter *.pdb -Recurse | Remove-Item -Force
Get-ChildItem $out -Filter createdump.exe -Recurse | Remove-Item -Force
Set-Content -Path (Join-Path $out "portable.flag") -Value "BuildnBits.Usage portable layout. Cache and settings are written to .\data\" -Encoding utf8NoBOM
Copy-Item (Join-Path $root "README.md") (Join-Path $out "README.md") -Force
Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE") -Force -ErrorAction SilentlyContinue

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
$sums = Join-Path $releaseDir "SHA256SUMS.txt"
Set-Content -Path $sums -Value "$hash  $zipName" -Encoding ascii
Copy-Item (Join-Path $root "docs\release-notes-v$version.md") (Join-Path $releaseDir "RELEASE-NOTES.md") -Force -ErrorAction SilentlyContinue

Write-Host "Portable folder: $out"
Write-Host "Release zip:     $zip"
Write-Host "SHA256:          $hash"
Write-Host "Attach the zip and SHA256SUMS.txt to the GitHub release. Do not commit artifacts/."
