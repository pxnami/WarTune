param([string]$Dotnet = 'dotnet', [string]$Compiler = 'zig', [switch]$Test, [switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit code $LASTEXITCODE" }
}
Invoke-Checked $Compiler @('cc','-shared','-O2','-o',"$artifacts\ArtIsWar.Dsp.dll","$projectRoot\native\dsp.c",'-ladvapi32')
Invoke-Checked $Dotnet @('build',"$projectRoot\src\ArtIsWar.App",'-c','Release')
if ($Test) {
    Invoke-Checked $Dotnet @('run','--project',"$projectRoot\tests\ArtIsWar.Tests",'-c','Release')
    Invoke-Checked $Compiler @('cc','-O2','-DDSP_TEST','-o',"$artifacts\dsp-tests.exe","$projectRoot\native\dsp.c",'-ladvapi32')
    Invoke-Checked "$artifacts\dsp-tests.exe" @()
    Invoke-Checked "$artifacts\dsp-tests.exe" @("$artifacts\ArtIsWar.Dsp.dll")
}
$release = Join-Path $projectRoot 'release'
$publishArguments = @('publish',"$projectRoot\src\ArtIsWar.App",'-c','Release','-o',$release)
if ($FrameworkDependent) { $publishArguments += @('--self-contained','false') }
else { $publishArguments += @('-r','win-x64','--self-contained','true') }
Invoke-Checked $Dotnet $publishArguments
Copy-Item -LiteralPath "$projectRoot\scripts\Install.ps1","$projectRoot\scripts\Uninstall.ps1","$projectRoot\README.md","$projectRoot\LICENSE" -Destination $release -Force
$previewDirectory = Join-Path $release 'docs\images'
New-Item -ItemType Directory -Force -Path $previewDirectory | Out-Null
Copy-Item -LiteralPath "$projectRoot\docs\images\wartune.png" -Destination $previewDirectory -Force
$packageFiles = @(Get-ChildItem -LiteralPath $release -File -Recurse | Where-Object Name -ne 'package-files.json' | ForEach-Object { $_.FullName.Substring($release.Length + 1) }) + @('package-files.json')
$packageFiles | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $release 'package-files.json') -Encoding UTF8
Write-Host "Release: $release"
