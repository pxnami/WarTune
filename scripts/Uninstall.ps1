$ErrorActionPreference = 'Stop'
$installPath = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\ArtIsWarAudio'))
$scriptPath = [IO.Path]::GetFullPath($PSScriptRoot)
if ($scriptPath -ne $installPath -or -not (Test-Path -LiteralPath (Join-Path $installPath '.artiswar-install'))) { throw 'Uninstall only runs from the verified WarTune install directory.' }
if (Get-Process WarTune,ArtIsWarAudio -ErrorAction SilentlyContinue) { throw 'Exit WarTune from its tray menu, then retry uninstall.' }
$manifestPath = Join-Path $installPath 'package-files.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Installed file manifest is missing; files were retained.' }
$owned = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json) + @('.artiswar-install')
$resolvedFiles = foreach ($name in $owned) {
    if ([IO.Path]::IsPathRooted($name)) { throw 'Invalid installed file path.' }
    $file = [IO.Path]::GetFullPath((Join-Path $installPath $name))
    if (-not $file.StartsWith($installPath + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Installed file path escapes the application directory.' }
    $file
}
$program = Join-Path $installPath 'WarTune.exe'
# The application removes only its owned APO include/files. UAC is requested for protected APO configuration.
$removal = Start-Process -FilePath $program -ArgumentList '--remove-config' -Verb RunAs -WindowStyle Hidden -Wait -PassThru
if ($removal.ExitCode -ne 0) { throw 'Audio configuration removal failed. Application files and backups were retained. Reboot and retry if a DSP DLL is still in use.' }
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ArtIsWarAudio' -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ArtIsWarAudio' -ErrorAction SilentlyContinue
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'WarTune.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
foreach ($file in $resolvedFiles) { if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file } }
$directories = Get-ChildItem -LiteralPath $installPath -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending
foreach ($directory in $directories) {
    $absolute = [IO.Path]::GetFullPath($directory.FullName)
    if ($absolute.StartsWith($installPath + '\',[StringComparison]::OrdinalIgnoreCase) -and -not (Get-ChildItem -LiteralPath $absolute -Force)) { Remove-Item -LiteralPath $absolute }
}
if (-not (Get-ChildItem -LiteralPath $installPath -Force)) { Remove-Item -LiteralPath $installPath }
Write-Host 'WarTune application and audio configuration removed. Personal profiles are retained in LocalAppData\ArtIsWarAudio.'
