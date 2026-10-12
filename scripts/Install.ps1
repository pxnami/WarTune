$ErrorActionPreference = 'Stop'
$installPath = Join-Path $env:LOCALAPPDATA 'Programs\ArtIsWarAudio'
$executable = Join-Path $installPath 'WarTune.exe'
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'WarTune.exe'))) { throw 'Run Install.ps1 from the extracted release package.' }
if (Get-Process WarTune,ArtIsWarAudio -ErrorAction SilentlyContinue) { throw 'Exit WarTune from its tray menu before installing or updating.' }
if ((Test-Path -LiteralPath $installPath) -and -not (Test-Path -LiteralPath (Join-Path $installPath '.artiswar-install'))) { throw 'The destination exists without an WarTune install marker. No files were changed.' }
New-Item -ItemType Directory -Force -Path $installPath | Out-Null
Set-Content -LiteralPath (Join-Path $installPath '.artiswar-install') -Value 'ArtIsWarAudio v1.0.0' -Encoding UTF8
$manifestPath = Join-Path $PSScriptRoot 'package-files.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Package file manifest is missing. Extract the complete release package.' }
$packageFiles = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($relative in $packageFiles) {
    if ([IO.Path]::IsPathRooted($relative)) { throw 'Invalid absolute package path.' }
    $source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relative))
    $destination = [IO.Path]::GetFullPath((Join-Path $installPath $relative))
    if (-not $source.StartsWith($PSScriptRoot + '\',[StringComparison]::OrdinalIgnoreCase) -or -not $destination.StartsWith($installPath + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Package path escapes its directory.' }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
}
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'WarTune.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath); $shortcut.TargetPath = $executable; $shortcut.WorkingDirectory = $installPath; $shortcut.Save()
$legacyShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'ArtIsWar Audio.lnk'
if (Test-Path -LiteralPath $legacyShortcut) {
    $previousShortcut = $shell.CreateShortcut($legacyShortcut)
    if ($previousShortcut.TargetPath -eq (Join-Path $installPath 'ArtIsWarAudio.exe')) { Remove-Item -LiteralPath $legacyShortcut }
}
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupValue = Get-ItemPropertyValue -LiteralPath $startupKey -Name 'ArtIsWarAudio' -ErrorAction SilentlyContinue
if ($startupValue -eq ('"' + (Join-Path $installPath 'ArtIsWarAudio.exe') + '" --tray')) {
    Set-ItemProperty -LiteralPath $startupKey -Name 'ArtIsWarAudio' -Value ('"' + $executable + '" --tray')
}
foreach ($legacyName in @('ArtIsWarAudio.exe','ArtIsWarAudio.dll','ArtIsWarAudio.deps.json','ArtIsWarAudio.runtimeconfig.json','ArtIsWarAudio.pdb')) {
    $legacyFile = Join-Path $installPath $legacyName
    if (Test-Path -LiteralPath $legacyFile -PathType Leaf) { Remove-Item -LiteralPath $legacyFile }
}
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ArtIsWarAudio'
New-Item -Path $uninstallKey -Force | Out-Null
$uninstaller = Join-Path $installPath 'Uninstall.ps1'
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'WarTune' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '1.0.0' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'WarTune' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installPath -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value $executable -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`"" -PropertyType String -Force | Out-Null
foreach ($name in @('NoModify','NoRepair')) { New-ItemProperty -Path $uninstallKey -Name $name -Value 1 -PropertyType DWord -Force | Out-Null }
Write-Host "Installed WarTune at $installPath. Windows startup remains disabled."
Write-Host 'The standalone release includes its .NET Desktop Runtime.'
Write-Host 'Launch from the Start menu and follow Audio Setup. Equalizer APO is installed separately with your consent.'
