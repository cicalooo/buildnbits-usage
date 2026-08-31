# Builds an unsigned MSIX from published tray + widget outputs.
# Optional signing: $env:BUILDNBITS_USAGE_PFX and $env:BUILDNBITS_USAGE_PFX_PASSWORD
# Private certificates are never committed.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $root "BuildnBits.Usage.sln"))) {
    $root = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $root = $root.Path
}
$packageDir = $PSScriptRoot
$layout = Join-Path $packageDir "layout"
$outDir = Join-Path $packageDir "AppPackages"
$msix = Join-Path $outDir "BuildnBits.Usage.msix"
$trayPub = Join-Path $packageDir "_pub-tray"
$widgetPub = Join-Path $packageDir "_pub-widget"

if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force -Path $layout, $outDir, (Join-Path $layout "Images"), (Join-Path $layout "Public") | Out-Null

dotnet publish (Join-Path $root "src\BuildnBits.Usage.Tray\BuildnBits.Usage.Tray.csproj") -c Release -r win-x64 --self-contained false -o $trayPub
dotnet publish (Join-Path $root "src\BuildnBits.Usage.Widgets\BuildnBits.Usage.Widgets.csproj") -c Release -r win-x64 --self-contained false -o $widgetPub

Copy-Item (Join-Path $trayPub "*") $layout -Recurse -Force
Copy-Item (Join-Path $widgetPub "*") $layout -Recurse -Force
Copy-Item (Join-Path $packageDir "Package.appxmanifest") (Join-Path $layout "AppxManifest.xml") -Force
Copy-Item (Join-Path $packageDir "Images\*") (Join-Path $layout "Images") -Force
Copy-Item (Join-Path $packageDir "Public\*") (Join-Path $layout "Public") -Force -ErrorAction SilentlyContinue
Remove-Item $trayPub, $widgetPub -Recurse -Force -ErrorAction SilentlyContinue

$makeappx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -match "x64" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $makeappx) {
    Write-Host "makeappx.exe not found. Install the Windows SDK or pack with Visual Studio (BuildnBits.Usage.Package.wapproj)."
    exit 2
}

& $makeappx.FullName pack /d $layout /p $msix /o
Write-Host "Packed $msix"

if ($env:BUILDNBITS_USAGE_PFX) {
    $signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match "x64" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe not found" }
    & $signtool.FullName sign /fd SHA256 /a /f $env:BUILDNBITS_USAGE_PFX /p $env:BUILDNBITS_USAGE_PFX_PASSWORD $msix
    Write-Host "Signed $msix with local PFX (not in source control)."
}
else {
    Write-Host "Unsigned package. See signing\README.md for a local CN=BuildnBits-Dev certificate."
}
