<#
.SYNOPSIS
    Builds V-Clicker, and optionally tests and packages it.

.EXAMPLE
    .\build.ps1                 # Release build -> src\bin\Release\net48\V-Clicker.exe
    .\build.ps1 -Test           # build + unit tests (safe: never touches mouse or keyboard)
    .\build.ps1 -Test -Package  # also writes dist\V-Clicker.exe, a .zip and a SHA-256 file
    .\build.ps1 -Integration    # also drives the real UI with real clicks (moves the cursor for a few seconds)
#>
param(
    [switch]$Test,
    [switch]$Integration,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is required to build V-Clicker: https://dotnet.microsoft.com/download'
}

function Invoke-Step([string]$What, [scriptblock]$Action) {
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." }
}

Invoke-Step 'Build' { dotnet build src\VClicker.csproj -c Release --nologo -v q }

if ($Test -or $Integration) {
    Invoke-Step 'Test build' { dotnet build tests\VClicker.Tests\VClicker.Tests.csproj -c Release --nologo -v q }
    $runner = 'tests\VClicker.Tests\bin\Release\net48\V-Clicker.Tests.exe'
    $arguments = @(); if ($Integration) { $arguments += '--integration' }
    Invoke-Step 'Tests' { & $runner @arguments }
}

if ($Package) {
    $version = ([xml](Get-Content src\VClicker.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $dist = Join-Path $PSScriptRoot 'dist'
    Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory $dist | Out-Null

    Copy-Item src\bin\Release\net48\V-Clicker.exe $dist
    $zip = Join-Path $dist "V-Clicker-$version-win.zip"
    Compress-Archive -Path (Join-Path $dist 'V-Clicker.exe'), LICENSE, README.md, README.en.md -DestinationPath $zip

    foreach ($file in @((Join-Path $dist 'V-Clicker.exe'), $zip)) {
        $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLower()
        "$hash  $(Split-Path $file -Leaf)" | Set-Content "$file.sha256" -Encoding ascii
    }
    Write-Host "Packaged V-Clicker $version in $dist"
}
