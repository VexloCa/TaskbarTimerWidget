[CmdletBinding()]
param(
    [switch]$StartAtLogin,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$sourceExecutable = Join-Path $PSScriptRoot 'TaskbarTimerWidget.exe'
if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    throw 'TaskbarTimerWidget.exe must be in the same directory as install.ps1.'
}

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
            throw 'Taskbar Timer Widget is running from the installation directory. Exit it from the right-click menu, then run the installer again.'
        }
    }
    finally {
        $process.Dispose()
    }
}

New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
Copy-Item -Force -LiteralPath $sourceExecutable -Destination $installedExecutable
foreach ($fileName in @(
    'uninstall.ps1',
    'README.md',
    'LICENSE',
    'CHANGELOG.md',
    'CONTRIBUTING.md',
    'SECURITY.md',
    'VERSION'
)) {
    $sourceFile = Join-Path $PSScriptRoot $fileName
    if (Test-Path -LiteralPath $sourceFile) {
        Copy-Item -Force -LiteralPath $sourceFile -Destination $installDirectory
    }
}
foreach ($directoryName in @('assets', 'docs')) {
    $sourceDirectory = Join-Path $PSScriptRoot $directoryName
    if (Test-Path -LiteralPath $sourceDirectory) {
        Copy-Item -Recurse -Force -LiteralPath $sourceDirectory -Destination $installDirectory
    }
}

$startMenuDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $startMenuDirectory 'Taskbar Timer Widget.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $null
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installedExecutable
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.IconLocation = $installedExecutable + ',0'
    $shortcut.Description = 'A lightweight countdown timer for the Windows taskbar.'
    $shortcut.Save()
}
finally {
    if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}

if ($StartAtLogin) {
    $runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -Path $runKeyPath -Name 'TaskbarTimerWidget' -Value ('"' + $installedExecutable + '" --startup') -Type String
}

Write-Host "Taskbar Timer Widget was installed to $installDirectory"
if ($StartAtLogin) { Write-Host 'Launch at sign-in was enabled.' }

if (-not $NoLaunch) {
    Start-Process -FilePath $installedExecutable
}
