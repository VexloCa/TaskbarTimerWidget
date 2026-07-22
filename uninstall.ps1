[CmdletBinding()]
param([switch]$KeepSettings)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$programsRoot = Join-Path $localAppData 'Programs'
$installDirectory = Join-Path $programsRoot 'TaskbarTimerWidget'
$installedExecutable = Join-Path $installDirectory 'TaskbarTimerWidget.exe'

$resolvedProgramsRoot = [System.IO.Path]::GetFullPath($programsRoot).TrimEnd('\') + '\'
$resolvedInstallDirectory = [System.IO.Path]::GetFullPath($installDirectory).TrimEnd('\') + '\'
if (-not $resolvedInstallDirectory.StartsWith($resolvedProgramsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The resolved installation directory is outside the expected per-user Programs directory.'
}

foreach ($process in Get-Process -Name 'TaskbarTimerWidget' -ErrorAction SilentlyContinue) {
    try {
        if ([string]::Equals($process.Path, $installedExecutable, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Taskbar Timer Widget is still running. Exit it from the right-click menu, then run the uninstaller again.'
        }
    }
    finally {
        $process.Dispose()
    }
}

$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runKey = Get-ItemProperty -Path $runKeyPath -ErrorAction SilentlyContinue
if ($null -ne $runKey -and $runKey.PSObject.Properties['TaskbarTimerWidget']) {
    $registeredCommand = [string]$runKey.TaskbarTimerWidget
    if ($registeredCommand.IndexOf($installedExecutable, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        Remove-ItemProperty -Path $runKeyPath -Name 'TaskbarTimerWidget' -ErrorAction SilentlyContinue
    }
}

$startMenuDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $startMenuDirectory 'Taskbar Timer Widget.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -Force -LiteralPath $shortcutPath
}

if (-not $KeepSettings) {
    Remove-Item -Recurse -Force -Path 'HKCU:\Software\TaskbarTimerWidget' -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -Recurse -Force -LiteralPath $installDirectory
}

Write-Host 'Taskbar Timer Widget was uninstalled.'
if ($KeepSettings) { Write-Host 'User settings were preserved.' }
