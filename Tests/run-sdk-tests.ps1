<#
.SYNOPSIS
    Runs the AMZN GoD SDK EditMode tests headless (Unity batchmode), the same code path as the pre-build gate.

.DESCRIPTION
    Calls AMZNGoDSDK.Editor.Tests.SdkTestGate.RunFromCommandLine. Exit code 0 = all tests passed and the
    pre-build gate is registered, non-zero = failures, compile errors or a Unity startup problem.

    The Unity Editor must be closed: batchmode cannot open a project that is already open.

    Every batchmode run is a new Editor session, so AnalyticsAppTypeSessionReset resets Analytics.AppType
    in Assets/Resources/amzn_god_sdk.json. The script backs the config up and restores it byte-for-byte
    if the run changed it.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Assets/AMZNGoDSDK/Tests/run-sdk-tests.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File run-sdk-tests.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\2022.3.60f1\Editor\Unity.exe"
#>
param(
    [string]$ProjectPath,
    [string]$UnityPath,
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = 'Stop'

if (-not $ProjectPath) {
    # Tests -> AMZNGoDSDK -> Assets -> project root
    $ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

$versionFile = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
if (-not (Test-Path $versionFile)) {
    Write-Error "Not a Unity project (no ProjectSettings\ProjectVersion.txt): $ProjectPath"
    exit 2
}

if (-not $UnityPath) {
    $versionLine = Get-Content $versionFile | Where-Object { $_ -match '^m_EditorVersion:\s*(\S+)' } | Select-Object -First 1
    $version = [regex]::Match($versionLine, '^m_EditorVersion:\s*(\S+)').Groups[1].Value
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
}

if (-not (Test-Path $UnityPath)) {
    Write-Error "Unity Editor not found: $UnityPath (pass -UnityPath)"
    exit 2
}

# The Editor holds Temp\UnityLockfile open while the project is open.
$lockFile = Join-Path $ProjectPath 'Temp\UnityLockfile'
if (Test-Path $lockFile) {
    try {
        $stream = [System.IO.File]::Open($lockFile, 'Open', 'ReadWrite', 'None')
        $stream.Close()
    }
    catch {
        Write-Error "The project is open in the Unity Editor. Close it and run again."
        exit 2
    }
}

$configPath = Join-Path $ProjectPath 'Assets\Resources\amzn_god_sdk.json'
$configBackup = $null
if (Test-Path $configPath) {
    $configBackup = [System.IO.File]::ReadAllBytes($configPath)
}

$logsDir = Join-Path $ProjectPath 'Logs'
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
$logPath = Join-Path $logsDir 'amzn-sdk-tests.log'
$resultsPath = Join-Path $logsDir 'amzn-sdk-tests.xml'
if (Test-Path $resultsPath) { Remove-Item $resultsPath -Force }

$arguments = @(
    '-batchmode', '-nographics', '-quit',
    '-projectPath', "`"$ProjectPath`"",
    '-executeMethod', 'AMZNGoDSDK.Editor.Tests.SdkTestGate.RunFromCommandLine',
    '-logFile', "`"$logPath`""
)

Write-Host "Unity:   $UnityPath"
Write-Host "Project: $ProjectPath"
Write-Host "Log:     $logPath"
Write-Host 'Running SDK tests in batchmode (import/compile can take a few minutes)...'

$exitCode = 1
try {
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -NoNewWindow
    # Caching the handle is required, otherwise ExitCode stays empty after Start-Process.
    $null = $process.Handle
    if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        $process.Kill()
        Write-Error "Unity did not finish in $TimeoutMinutes minutes; killed."
        $exitCode = 3
    }
    else {
        $exitCode = $process.ExitCode
        if ($null -eq $exitCode) { $exitCode = 1 }
    }
}
finally {
    if ($null -ne $configBackup) {
        $current = $null
        if (Test-Path $configPath) { $current = [System.IO.File]::ReadAllBytes($configPath) }
        $same = ($null -ne $current) -and ($current.Length -eq $configBackup.Length) -and
                (-not (Compare-Object $current $configBackup -SyncWindow 0))
        if (-not $same) {
            [System.IO.File]::WriteAllBytes($configPath, $configBackup)
            Write-Host 'amzn_god_sdk.json was changed by the Editor session (App Type reset); restored from backup.'
        }
    }
}

if (Test-Path $logPath) {
    Write-Host ''
    Get-Content $logPath -Encoding UTF8 | Where-Object {
        $_ -match '\[AMZN GoD SDK\] \[Tests\]' -or $_ -match '^\s+\u2022 ' -or $_ -match 'error CS\d+' -or $_ -match 'executeMethod'
    } | Select-Object -First 80 | ForEach-Object { Write-Host $_ }
}

if (Test-Path $resultsPath) { Write-Host "Results: $resultsPath" }

if ($exitCode -eq 0) { Write-Host 'SDK tests: PASSED' } else { Write-Host "SDK tests: FAILED (exit code $exitCode)" }
exit $exitCode
