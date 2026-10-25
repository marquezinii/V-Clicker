<#
.SYNOPSIS
    Installs the Store layout (dist\store\layout) as a development package and checks, inside the package, what
    docs/microsoft-store.md lists as risks: saving where AppData is virtualized, the Recycle Bin, the command line and,
    with -Elevation, "Restart as administrator". Removes the package at the end. Needs Developer Mode and Windows
    PowerShell (powershell.exe).

.EXAMPLE
    powershell -File .\scripts\msix\test-store-msix.ps1               # storage, Recycle Bin, command line
    powershell -File .\scripts\msix\test-store-msix.ps1 -Elevation    # also "Restart as administrator": accept the UAC prompt
#>
param(
    [string]$Layout = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'dist\store\layout'),
    [switch]$Elevation,

    # Internal: the script runs itself inside the package (Invoke-CommandInDesktopPackage) with these.
    [string]$Probe,
    [string]$ResultFile,
    [string]$Stamp
)

$ErrorActionPreference = 'Stop'
$testDir = Join-Path $env:APPDATA 'V-Clicker-MsixTest'
$real = Join-Path $testDir 'real'
$fresh = Join-Path $testDir 'fresh'

# ---------------------------------------------------------------------------------------------- inside the package
if ($Probe -eq 'Storage') {
    Add-Type -AssemblyName Microsoft.VisualBasic
    $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $Layout 'V-Clicker.exe'))
    $settingsType = $assembly.GetType('VClicker.ClickSettings', $true)
    $found = [ordered]@{}

    function Invoke-Probe([string]$Name, [scriptblock]$Body) {
        try { $found[$Name] = [string](& $Body) } catch { $found[$Name] = 'FAIL ' + $_.Exception.Message }
    }

    # The app's own ClickSettings.Save (temp file + File.Replace) and Load, reached by reflection.
    function Save-Setting([string]$Path, [double]$Interval) {
        $settings = [Activator]::CreateInstance($settingsType, $true)
        $settings.IntervalMs = $Interval
        $saved = $settings.Save($Path)
        $back = $settingsType.GetMethod('Load').Invoke($null, @($Path)).IntervalMs
        $verdict = if ($saved -and $back -eq $Interval) { 'OK' } else { 'FAIL' }
        "$verdict saved=$saved wrote=$Interval read-back=$back"
    }

    Invoke-Probe 'save over a settings.ini that exists outside the package' { Save-Setting (Join-Path $real 'settings.ini') 111 }
    Invoke-Probe 'save a settings.ini that did not exist' { Save-Setting (Join-Path $fresh 'settings.ini') 222 }
    Invoke-Probe 'save it again (replace over the new file)' { Save-Setting (Join-Path $fresh 'settings.ini') 333 }
    Invoke-Probe 'rename a macro that exists outside the package' {
        [IO.File]::Move((Join-Path $real 'rename-me.vcmacro'), (Join-Path $real 'renamed.vcmacro'))
        $ok = (Test-Path (Join-Path $real 'renamed.vcmacro')) -and -not (Test-Path (Join-Path $real 'rename-me.vcmacro'))
        $(if ($ok) { 'OK' } else { 'FAIL' }) + ' renamed'
    }
    Invoke-Probe 'rename a macro created inside the package' {
        $from = Join-Path $fresh 'new.vcmacro'
        [IO.File]::WriteAllText($from, 'x')
        [IO.File]::Move($from, (Join-Path $fresh 'new-renamed.vcmacro'))
        $ok = (Test-Path (Join-Path $fresh 'new-renamed.vcmacro')) -and -not (Test-Path $from)
        $(if ($ok) { 'OK' } else { 'FAIL' }) + ' renamed'
    }
    # The Macro Studio's delete: FileSystem.DeleteFile(path, OnlyErrorDialogs, SendToRecycleBin).
    foreach ($kind in @(@('real', $real), @('virtualized', $fresh))) {
        Invoke-Probe "delete a macro to the Recycle Bin ($($kind[0]))" {
            $path = Join-Path $kind[1] "recycle-$($kind[0])-$Stamp.vcmacro"
            if ($kind[0] -eq 'virtualized') { [IO.File]::WriteAllText($path, 'x') }
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($path, 'OnlyErrorDialogs', 'SendToRecycleBin')
            $(if (Test-Path $path) { 'FAIL still there' } else { 'OK gone from the folder' })
        }
    }
    $found | ConvertTo-Json | Set-Content -Path $ResultFile -Encoding UTF8
    return
}

if ($Probe -eq 'Elevate') {
    # What MainForm.RestartAsAdministrator does: the package's own .exe with the runas verb.
    $info = New-Object Diagnostics.ProcessStartInfo -ArgumentList (Join-Path $Layout 'V-Clicker.exe'), '--wait-previous'
    $info.UseShellExecute = $true
    $info.Verb = 'runas'
    try { [Diagnostics.Process]::Start($info) | Out-Null; $outcome = 'started' }
    catch [ComponentModel.Win32Exception] { $outcome = "Win32Exception $($_.Exception.NativeErrorCode): $($_.Exception.Message)" }
    @{ outcome = $outcome } | ConvertTo-Json | Set-Content -Path $ResultFile -Encoding UTF8
    return
}

# ---------------------------------------------------------------------------------------------- outside
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this with Windows PowerShell (powershell.exe).' }
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$manifestPath = Join-Path $Layout 'AppxManifest.xml'
if (-not (Test-Path $manifestPath)) { throw "No package layout at ${Layout}: run scripts\msix\build-store-msix.ps1 first." }
$name = ([xml](Get-Content $manifestPath -Raw)).Package.Identity.Name
if (Get-AppxPackage -Name $name) { throw "$name is already installed: remove it first (Remove-AppxPackage), this test must not touch it." }
if (Get-Process -Name 'V-Clicker' -ErrorAction SilentlyContinue) { throw 'Close V-Clicker first: only one instance runs at a time.' }
if (Test-Path $testDir) { throw "$testDir exists from an earlier run: look at it and delete it, this test only removes what it created." }

$script:failed = 0
function Write-Check([string]$Label, [bool]$Ok, [string]$Detail) {
    if (-not $Ok) { $script:failed++ }
    Write-Host ("[{0}] {1} - {2}" -f $(if ($Ok) { 'ok  ' } else { 'FAIL' }), $Label, $Detail)
}

$results = Join-Path $root 'dist\store\results'
New-Item -ItemType Directory $results -Force | Out-Null
$stamp = [guid]::NewGuid().ToString('N').Substring(0, 8)

# Files that exist outside the package, as for someone who used the .exe before installing the Store version.
New-Item -ItemType Directory $real | Out-Null
Set-Content -Path (Join-Path $real 'settings.ini') -Value 'IntervalMs=50'
Set-Content -Path (Join-Path $real 'rename-me.vcmacro') -Value 'x'
Set-Content -Path (Join-Path $real "recycle-real-$stamp.vcmacro") -Value 'x'

$package = $null
try {
    Add-AppxPackage -Register $manifestPath
    $package = Get-AppxPackage -Name $name

    function Invoke-InPackage([string]$ProbeName, [int]$TimeoutSeconds) {
        $file = Join-Path $results "$ProbeName.json"
        Remove-Item $file -ErrorAction SilentlyContinue
        $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Layout `"$Layout`" -Probe $ProbeName -ResultFile `"$file`" -Stamp $stamp"
        Invoke-CommandInDesktopPackage -PackageFamilyName $package.PackageFamilyName -AppId 'VClicker' -Command $powershell -Args $arguments
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while (-not (Test-Path $file) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        if (-not (Test-Path $file)) { throw "The $ProbeName probe wrote no result in $TimeoutSeconds s." }
        Start-Sleep -Milliseconds 500
        Get-Content $file -Raw | ConvertFrom-Json
    }

    Write-Host "Installed $($package.PackageFullName) from $Layout"

    # 1. Saving and deleting where AppData is virtualized (the app's own code, run inside the package).
    $storage = Invoke-InPackage 'Storage' 120
    foreach ($entry in $storage.PSObject.Properties) { Write-Check $entry.Name ($entry.Value -like 'OK*') $entry.Value }
    # Where the files really are, seen from outside the package.
    $cache = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalCache"
    $privateCopies = @(Get-ChildItem $cache -Recurse -Filter 'settings.ini' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    Write-Host ("  outside the package: the existing settings.ini now says {0}; the new one is in the real folder: {1}; in the package's private cache: {2}" -f
        (Select-String -Path (Join-Path $real 'settings.ini') -Pattern '^IntervalMs=').Line,
        (Test-Path (Join-Path $fresh 'settings.ini')), $(if ($privateCopies) { $privateCopies -join ', ' } else { 'no' }))

    $bin = (New-Object -ComObject Shell.Application).NameSpace(10)
    $binned = @($bin.Items() | ForEach-Object { $_.Name })
    foreach ($kind in 'real', 'virtualized') {
        $seen = @($binned | Where-Object { $_ -like "recycle-$kind-$stamp*" }).Count -gt 0
        Write-Check "Recycle Bin holds the deleted $kind macro" $seen "recycle-$kind-$stamp.vcmacro"
    }

    # 2. The command line: the package's .exe by path, by alias, and the line "Copy command" produces.
    $exePath = Join-Path $package.InstallLocation 'V-Clicker.exe'
    $help = Start-Process -FilePath $exePath -ArgumentList '--help' -Wait -PassThru
    Write-Check 'by path: --help' ($help.ExitCode -eq 0) "exit code $($help.ExitCode), expected 0"
    $bad = Start-Process -FilePath $exePath -ArgumentList '--no-such-option' -Wait -PassThru
    Write-Check 'by path: bad argument' ($bad.ExitCode -eq 2) "exit code $($bad.ExitCode), expected 2"
    $viaAlias = Start-Process -FilePath 'V-Clicker.exe' -ArgumentList '--help' -Wait -PassThru
    Write-Check 'by alias (V-Clicker.exe on PATH): --help' ($viaAlias.ExitCode -eq 0) "exit code $($viaAlias.ExitCode), expected 0"

    $macro = Join-Path $testDir 'cli-test.vcmacro'
    Set-Content -Path $macro -Value "V-Clicker macro 1`nname=cli-test`nexe=v-clicker-msix-test.exe`nrepeat=1`n[steps]`nwait 100 recorded=100"
    $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $Layout 'V-Clicker.exe'))
    # Typed arguments: PowerShell's own wrappers do not convert on a reflection call.
    $callArgs = [object[]]::new(3)
    $callArgs[0] = [string]$exePath
    $callArgs[1] = [string]$macro
    $callArgs[2] = [Collections.Generic.List[string]]::new()
    $line = $assembly.GetType('VClicker.MacroStudio', $true).GetMethod('CommandLineFor', [Reflection.BindingFlags]'NonPublic,Static').
        Invoke($null, $callArgs)
    $job = Start-Job -ScriptBlock {
        param($Text)
        & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -Command $Text 2>&1
    } -ArgumentList $line
    if (Wait-Job $job -Timeout 45) {
        $last = @(Receive-Job $job)[-1]
        Write-Check 'Copy command: the line it produces runs the packaged .exe' ("$last" -match '^\d+$') "last output line: $last (4 = the macro's app is not open, as intended)"
    }
    else {
        Get-Process -Name 'V-Clicker' -ErrorAction SilentlyContinue | Stop-Process
        Write-Check 'Copy command: the line it produces runs the packaged .exe' $false 'no exit code in 45 s'
    }
    Remove-Job $job -Force

    # 3. Restart as administrator (asks for the UAC consent: a person has to answer it).
    if ($Elevation) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class VClickerProbe
{
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPackageFullName(IntPtr process, ref int length, StringBuilder name);
    [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token, int kind, out int info, int length, out int returned);

    public static string PackageOf(int id)
    {
        IntPtr process = OpenProcess(0x1000, false, id); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero) return "cannot open";
        try
        {
            int length = 256;
            var name = new StringBuilder(length);
            int status = GetPackageFullName(process, ref length, name);
            return status == 0 ? name.ToString() : "none (error " + status + ")";
        }
        finally { CloseHandle(process); }
    }

    public static string ElevatedOf(int id)
    {
        IntPtr process = OpenProcess(0x1000, false, id), token;
        if (process == IntPtr.Zero) return "cannot open";
        try
        {
            if (!OpenProcessToken(process, 8, out token)) return "cannot read the token";
            try
            {
                int info, returned;
                return GetTokenInformation(token, 20, out info, 4, out returned) ? (info != 0).ToString() : "cannot read"; // TokenElevation
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }
}
'@
        Write-Host 'Accept the UAC prompt now (the test waits about 90 s).'
        $elevate = Invoke-InPackage 'Elevate' 90
        if ($elevate.outcome -ne 'started') { Write-Check 'Restart as administrator: runas on the packaged .exe' $false $elevate.outcome }
        else {
            $deadline = (Get-Date).AddSeconds(20)
            $running = $null
            while (-not $running -and (Get-Date) -lt $deadline) {
                $running = Get-Process -Name 'V-Clicker' -ErrorAction SilentlyContinue | Select-Object -First 1
                Start-Sleep -Milliseconds 500
            }
            if (-not $running) { Write-Check 'Restart as administrator: runas on the packaged .exe' $false 'started, but no V-Clicker process appeared' }
            else {
                $isElevated = [VClickerProbe]::ElevatedOf($running.Id)
                $identity = [VClickerProbe]::PackageOf($running.Id)
                Write-Check 'Restart as administrator: runas on the packaged .exe' (($isElevated -eq 'True') -and ($identity -like "$name*")) "elevated=$isElevated, package=$identity"
                $running.CloseMainWindow() | Out-Null # an elevated window may ignore this from here; if it stays, close it by hand
            }
        }
    }
}
finally {
    if ($package) { Remove-AppxPackage -Package $package.PackageFullName }
    Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue # created above; the script refuses to start if it already exists
}

Write-Host $(if ($script:failed) { "$script:failed check(s) failed." } else { 'All checks passed.' })
if ($script:failed) { exit 1 }
