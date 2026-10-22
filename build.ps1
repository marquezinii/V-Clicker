<#
.SYNOPSIS
    Builds V-Clicker, and optionally lints, tests and packages it. The CI (docs/ci-cd.md) runs exactly these switches.

.EXAMPLE
    .\build.ps1                     # Release build -> src\bin\Release\net48\V-Clicker.exe
    .\build.ps1 -Lint               # formatting (dotnet format) and PowerShell (PSScriptAnalyzer) checks, then the build
    .\build.ps1 -Test               # build + unit tests (safe: never touches mouse or keyboard)
    .\build.ps1 -Test -Coverage     # the same under a coverage collector -> TestResults\coverage\index.html
    .\build.ps1 -Integration        # also drives the real UI with real clicks (moves the cursor for a few seconds)
    .\build.ps1 -Test -Package      # also writes dist\V-Clicker.exe, a .zip and SHA-256 files, and smoke-tests the .exe
#>
param(
    [switch]$Lint,
    [switch]$Test,
    [switch]$Integration,
    [switch]$Coverage,
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

if ($Lint) {
    # Whitespace, code style and analyzer fixers, as configured in .editorconfig; fails on anything it would change.
    Invoke-Step 'Formatting check (dotnet format)' { dotnet format V-Clicker.sln --verify-no-changes }

    if (-not (Get-Module -ListAvailable PSScriptAnalyzer)) {
        throw 'PSScriptAnalyzer is required for -Lint: Install-Module PSScriptAnalyzer -Scope CurrentUser'
    }
    $scripts = Get-ChildItem build.ps1, scripts -Recurse -Include *.ps1, *.psm1, *.psd1
    $findings = $scripts | ForEach-Object { Invoke-ScriptAnalyzer -Path $_.FullName -Settings .\PSScriptAnalyzerSettings.psd1 }
    if ($findings) {
        $findings | Format-Table ScriptName, Line, RuleName, Message -AutoSize -Wrap | Out-String | Write-Host
        throw "PSScriptAnalyzer: $(@($findings).Count) finding(s)."
    }
    Write-Host "Lint: $(@($scripts).Count) PowerShell file(s) clean, formatting verified."
}

Invoke-Step 'Build' { dotnet build src\VClicker.csproj -c Release --nologo -v q }

if ($Test -or $Integration) {
    $arguments = @(); if ($Integration) { $arguments += '--integration' }

    if ($Coverage) {
        # dotnet-coverage and ReportGenerator are pinned in .config/dotnet-tools.json. The collector only measures modules
        # that have a PDB, and the shipped .exe has none (DebugType none), so the tests and the app are built again with
        # portable PDBs into TestResults, leaving src\bin as -Package expects it. Only the app's assembly is reported.
        $results = Join-Path $PSScriptRoot 'TestResults'
        $bin = Join-Path $results 'coverage-build'
        Invoke-Step 'Coverage build' {
            dotnet build tests\VClicker.Tests\VClicker.Tests.csproj -c Release --nologo -v q -p:DebugType=portable "-p:OutDir=$bin/"
        }
        Invoke-Step 'Tool restore' { dotnet tool restore }
        $raw = Join-Path $results 'coverage.cobertura.xml'
        $log = dotnet dotnet-coverage collect -f cobertura -o $raw (Join-Path $bin 'V-Clicker.Tests.exe') @arguments
        $log | Write-Host
        # Belt and braces: dotnet-coverage relays the runner's exit code, and the runner's own verdict line must be there too.
        if ($LASTEXITCODE -ne 0 -or -not ($log -match '^PASS: \d+ tests$')) { throw 'Tests (with coverage) failed: see the output above.' }
        Invoke-Step 'Coverage report' {
            dotnet reportgenerator "-reports:$raw" "-targetdir:$results\coverage" '-reporttypes:Html;MarkdownSummaryGithub;TextSummary' `
                '-assemblyfilters:-V-Clicker.Tests' '-title:V-Clicker'
        }
        Get-Content "$results\coverage\Summary.txt" | Select-Object -Skip 1 -First 10 | Write-Host
        Write-Host "Coverage report: $results\coverage\index.html"
    }
    else {
        Invoke-Step 'Test build' { dotnet build tests\VClicker.Tests\VClicker.Tests.csproj -c Release --nologo -v q }
        Invoke-Step 'Tests' { & 'tests\VClicker.Tests\bin\Release\net48\V-Clicker.Tests.exe' @arguments }
    }
}

if ($Package) {
    $version = ([xml](Get-Content src\VClicker.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $dist = Join-Path $PSScriptRoot 'dist'
    Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory $dist | Out-Null

    Copy-Item src\bin\Release\net48\V-Clicker.exe $dist
    $exe = Join-Path $dist 'V-Clicker.exe'

    # Smoke test: the packaged .exe must start, read its command line and exit with the codes docs/developer.md promises.
    $help = Start-Process $exe -ArgumentList '--help' -NoNewWindow -Wait -PassThru
    if ($help.ExitCode -ne 0) { throw "Smoke test: 'V-Clicker.exe --help' exited with $($help.ExitCode), expected 0." }
    $bad = Start-Process $exe -ArgumentList '--no-such-option' -NoNewWindow -Wait -PassThru
    if ($bad.ExitCode -ne 2) { throw "Smoke test: an unknown argument exited with $($bad.ExitCode), expected 2." }

    $zip = Join-Path $dist "V-Clicker-$version-win.zip"
    Compress-Archive -Path $exe, LICENSE, README.md, README.en.md -DestinationPath $zip

    foreach ($file in @($exe, $zip)) {
        $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLower()
        "$hash  $(Split-Path $file -Leaf)" | Set-Content "$file.sha256" -Encoding ascii
    }
    Write-Host "Packaged V-Clicker $version in $dist (smoke test passed)"
}
