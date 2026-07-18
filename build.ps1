[CmdletBinding()]
param(
    [switch]$RunSmokeTest,
    [switch]$Package,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$versionFile = Join-Path $PSScriptRoot 'VERSION'
if ([string]::IsNullOrWhiteSpace($Version)) {
    if (-not (Test-Path -LiteralPath $versionFile)) {
        throw 'VERSION file was not found.'
    }

    $Version = (Get-Content -Raw -LiteralPath $versionFile).Trim()
}

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a valid semantic version (for example, 1.2.3 or 1.2.3-beta.1)."
}

$coreVersion = ($Version -split '[-+]')[0]
$versionParts = $coreVersion.Split('.') | ForEach-Object { [int]$_ }
if (@($versionParts | Where-Object { $_ -gt 65534 }).Count -gt 0) {
    throw 'Each numeric version component must be between 0 and 65534.'
}
$assemblyVersion = $coreVersion + '.0'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework C# compiler was not found. Install .NET Framework 4.8 Developer Pack.'
}

$outputDirectory = Join-Path $PSScriptRoot 'build'
$artifactDirectory = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$generatedVersionSource = Join-Path $outputDirectory 'GeneratedAssemblyVersion.cs'
$versionSourceText = @"
using System.Reflection;
[assembly: AssemblyVersion("$assemblyVersion")]
[assembly: AssemblyFileVersion("$assemblyVersion")]
[assembly: AssemblyInformationalVersion("$Version")]
"@
[System.IO.File]::WriteAllText($generatedVersionSource, $versionSourceText, [System.Text.UTF8Encoding]::new($false))

$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -Recurse |
    Sort-Object FullName |
    ForEach-Object FullName

$commonArguments = @(
    '/nologo',
    '/optimize+',
    '/platform:anycpu',
    '/warn:4',
    '/warnaserror+',
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
)

$applicationPath = Join-Path $outputDirectory 'TaskbarTimerWidget.exe'
$applicationArguments = $commonArguments + @(
    '/target:winexe',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')),
    ('/win32icon:' + (Join-Path $PSScriptRoot 'assets\app-icon.ico')),
    ('/out:' + $applicationPath)
) + $sourceFiles + $generatedVersionSource
& $compiler $applicationArguments
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }

$logicSources = @(
    (Join-Path $PSScriptRoot 'src\Models\TimerModels.cs'),
    (Join-Path $PSScriptRoot 'src\Native\Win32Interop.cs'),
    (Join-Path $PSScriptRoot 'src\Services\TaskbarDockingService.cs'),
    (Join-Path $PSScriptRoot 'tests\WidgetLogicTests.cs')
)
$logicTestPath = Join-Path $outputDirectory 'WidgetLogicTests.exe'
$testArguments = $commonArguments + @(
    '/target:exe',
    ('/out:' + $logicTestPath)
) + $logicSources
& $compiler $testArguments
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }

& $logicTestPath
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$smokeTestPath = Join-Path $outputDirectory 'WidgetSmokeTests.exe'
$smokeArguments = $commonArguments + @(
    '/target:exe',
    '/main:WidgetSmokeTests',
    ('/out:' + $smokeTestPath)
) + $sourceFiles + $generatedVersionSource + (Join-Path $PSScriptRoot 'tests\WidgetSmokeTests.cs')
& $compiler $smokeArguments
if ($LASTEXITCODE -ne 0) { throw 'Smoke-test build failed.' }

if ($RunSmokeTest) {
    & $smokeTestPath
    if ($LASTEXITCODE -ne 0) { throw 'Smoke test failed.' }
}

Copy-Item -Force -LiteralPath $applicationPath -Destination (Join-Path $PSScriptRoot 'TaskbarTimerWidget.exe')

if ($Package) {
    New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
    $packageRoot = Join-Path $outputDirectory 'package'
    $packageDirectory = Join-Path $packageRoot 'TaskbarTimerWidget'
    if (Test-Path -LiteralPath $packageRoot) {
        Remove-Item -Recurse -Force -LiteralPath $packageRoot
    }
    New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null

    Copy-Item -LiteralPath $applicationPath -Destination $packageDirectory
    foreach ($fileName in @(
        'install.ps1',
        'uninstall.ps1',
        'README.md',
        'LICENSE',
        'CHANGELOG.md',
        'CONTRIBUTING.md',
        'SECURITY.md',
        'VERSION'
    )) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $fileName) -Destination $packageDirectory
    }
    foreach ($directoryName in @('assets', 'docs')) {
        Copy-Item -Recurse -LiteralPath (Join-Path $PSScriptRoot $directoryName) -Destination $packageDirectory
    }

    $archiveName = "TaskbarTimerWidget-$Version-win.zip"
    $archivePath = Join-Path $artifactDirectory $archiveName
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -Force -LiteralPath $archivePath
    }
    Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal

    $checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = $archivePath + '.sha256'
    $checksumText = $checksum + ' *' + $archiveName + [Environment]::NewLine
    [System.IO.File]::WriteAllText($checksumPath, $checksumText, [System.Text.Encoding]::ASCII)
    Write-Host "Release package: $archivePath"
    Write-Host "SHA-256: $checksum"
}

Write-Host "Build $Version and tests completed successfully."
