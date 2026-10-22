<#
.SYNOPSIS
    Prepares a release: writes the new version into src\VClicker.csproj and turns the "Unreleased" section of
    CHANGELOG.md into that version's section, dated today. It changes files only; committing, tagging and pushing is
    done by the "Prepare release" workflow (gh workflow run release-prepare.yml -f version=1.1.0) or by you.

.EXAMPLE
    .\scripts\prepare-release.ps1 -Version 1.1.0
    git commit -am "Release v1.1.0"; git tag -a v1.1.0 -m "V-Clicker v1.1.0"; git push origin main v1.1.0
#>
param(
    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "The version must be MAJOR.MINOR.PATCH (got '$Version')." }

$csprojPath = Join-Path $root 'src\VClicker.csproj'
$csproj = Get-Content $csprojPath -Raw
$current = ([xml]$csproj).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ([version]$Version -le [version]$current) { throw "The version must be greater than the current $current (got $Version)." }
if (git -C $root tag --list "v$Version") { throw "The tag v$Version already exists." }

$changelogPath = Join-Path $root 'CHANGELOG.md'
$changelog = Get-Content $changelogPath -Raw
$unreleased = [regex]::Match($changelog, '(?ms)^## \[Unreleased\][^\n]*\n(.*?)(?=^## \[)')
if (-not $unreleased.Success) { throw 'CHANGELOG.md needs a "## [Unreleased]" section followed by the previous version.' }
if ($unreleased.Groups[1].Value -notmatch '(?m)^- ') { throw 'CHANGELOG.md: the Unreleased section lists no change; write the release notes first.' }

$date = (Get-Date).ToString('yyyy-MM-dd')
$changelog = $changelog -replace '(?m)^## \[Unreleased\][^\n]*\n', "## [Unreleased]`n`n## [$Version] - $date`n"
$csproj = $csproj -replace "<Version>$([regex]::Escape($current))</Version>", "<Version>$Version</Version>"

# UTF-8 without BOM in every PowerShell (Set-Content -Encoding utf8 writes a BOM in Windows PowerShell).
[IO.File]::WriteAllText($changelogPath, $changelog)
[IO.File]::WriteAllText($csprojPath, $csproj)
Write-Host "Prepared V-Clicker $Version (was $current): src\VClicker.csproj and CHANGELOG.md updated. Review, commit, tag v$Version and push."
