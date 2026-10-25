<#
.SYNOPSIS
    Packages dist\V-Clicker.exe as the Microsoft Store MSIX (docs/microsoft-store.md). It touches nothing in the
    repository: the package layout and the .msix go to dist\store.

.EXAMPLE
    .\build.ps1 -Package
    .\scripts\msix\build-store-msix.ps1 -PackageName 'VemryxInc.V-Clicker' -Publisher 'CN=...' -PublisherDisplayName 'Vemryx Inc.'

    The three identity values are the ones Partner Center shows under Product identity; without them the template's
    are used, which is fine for a local test but not for the upload.
#>
param(
    [string]$Exe = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'dist\V-Clicker.exe'),
    [string]$PackageName,
    [string]$Publisher,
    [string]$PublisherDisplayName
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

$csprojVersion = ([xml](Get-Content (Join-Path $root 'src\VClicker.csproj'))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
$version = "$csprojVersion.0" # the Store keeps the fourth number for itself

if (-not (Test-Path $Exe)) { throw "No executable at ${Exe}: run .\build.ps1 -Package first." }
$productVersion = (Get-Item $Exe).VersionInfo.ProductVersion
if (-not $productVersion.StartsWith($csprojVersion)) {
    throw "$Exe is version $productVersion but src\VClicker.csproj says ${csprojVersion}: build it again."
}

$makeAppx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object FullName -Like '*\x64\makeappx.exe' | Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $makeAppx) { throw 'makeappx.exe not found: install the Windows 10/11 SDK.' }

$out = Join-Path $root 'dist\store'
$layout = Join-Path $out 'layout'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory (Join-Path $layout 'Assets') | Out-Null
Copy-Item $Exe $layout # the very file the GitHub release ships: same bytes, same SHA-256

Add-Type -AssemblyName System.Drawing
function Export-SquarePng([string]$Source, [string]$Destination, [int]$Size) {
    $image = [System.Drawing.Image]::FromFile($Source)
    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($image, 0, 0, $Size, $Size)
        $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose(); $bitmap.Dispose(); $image.Dispose()
    }
}

$logo = Join-Path $root 'assets\icon-256.png'
$assets = Join-Path $layout 'Assets'
Export-SquarePng $logo (Join-Path $assets 'StoreLogo.png') 50
Export-SquarePng $logo (Join-Path $assets 'Square44x44Logo.png') 44
Export-SquarePng $logo (Join-Path $assets 'Square150x150Logo.png') 150
# Without the unplated variants Windows paints a grey plate behind the transparent corners of the taskbar icon.
foreach ($size in 16, 24, 32, 48, 256) {
    foreach ($form in 'unplated', 'lightunplated') {
        Export-SquarePng $logo (Join-Path $assets "Square44x44Logo.targetsize-${size}_altform-$form.png") $size
    }
}

[xml]$manifest = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
$manifest.Package.Identity.Version = $version
if ($PackageName) { $manifest.Package.Identity.Name = $PackageName }
if ($Publisher) { $manifest.Package.Identity.Publisher = $Publisher }
if ($PublisherDisplayName) { $manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName }
if (-not ($PackageName -and $Publisher -and $PublisherDisplayName)) {
    Write-Warning 'Using the template identity (scripts\msix\AppxManifest.xml): pass the exact Partner Center values for the upload.'
}
$manifest.Save((Join-Path $layout 'AppxManifest.xml'))

$package = Join-Path $out "V-Clicker-Store-$version.msix"
& $makeAppx pack /d $layout /p $package /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed (exit code $LASTEXITCODE)." }

Write-Host "Store package ${version}: $package"
Write-Host 'Upload it unsigned: the Store signs it after certification. Try it locally with scripts\msix\test-store-msix.ps1.'
