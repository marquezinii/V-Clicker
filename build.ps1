param([switch]$Test)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$references = @('/r:System.Windows.Forms.dll', '/r:System.Drawing.dll')
& $compiler /nologo /target:winexe /platform:x64 /optimize+ $references "/out:$output\AutoClicker.exe" "$PSScriptRoot\AutoClicker.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar AutoClicker.' }
if ($Test) {
    & $compiler /nologo /target:exe /platform:x64 /main:AutoClickerTests $references "/out:$output\AutoClicker.Tests.exe" "$PSScriptRoot\AutoClicker.cs" "$PSScriptRoot\tests\AutoClickerTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar testes.' }
    & "$output\AutoClicker.Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes.' }
}
