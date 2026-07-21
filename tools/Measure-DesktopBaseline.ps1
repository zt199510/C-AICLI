[CmdletBinding()]
param(
    [string]$PackageRoot,
    [string]$OutputPath,
    [ValidateRange(1, 20)][int]$Runs = 5,
    [ValidateRange(250, 30000)][int]$ReadySampleMilliseconds = 3000,
    [ValidateRange(0, 120)][int]$IdleSeconds = 30,
    [ValidateRange(1, 30)][int]$SampleIntervalSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    $PackageRoot = Join-Path $repoRoot "apps\desktop\out\C-AICLI Desktop-win32-x64"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $artifactsRoot "desktop-performance\week76-performance.json"
}

function Test-PathWithin([string]$Root, [string]$Candidate) {
    $separators = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd($separators)
    $candidatePath = [System.IO.Path]::GetFullPath($Candidate).TrimEnd($separators)
    return $candidatePath.Equals($rootPath, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidatePath.StartsWith($rootPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

$resolvedPackage = [System.IO.Path]::GetFullPath($PackageRoot)
$desktopOut = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "apps\desktop\out"))
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
if (-not (Test-PathWithin $desktopOut $resolvedPackage)) { throw "Desktop package must remain under apps\desktop\out." }
if (-not (Test-PathWithin $artifactsRoot $resolvedOutput)) { throw "Performance evidence must remain under artifacts." }

$desktopExe = Join-Path $resolvedPackage "caicli-desktop.exe"
$asar = Join-Path $resolvedPackage "resources\app.asar"
$appHost = Join-Path $resolvedPackage "resources\apphost\CSharpAiCli.AppHost.exe"
foreach ($required in @($desktopExe, $asar, $appHost)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Packaged Desktop payload is missing: $required" }
}

function Get-ProcessTree([int]$RootPid) {
    $all = @(Get-CimInstance Win32_Process)
    $ids = [System.Collections.Generic.HashSet[int]]::new()
    [void]$ids.Add($RootPid)
    do {
        $added = $false
        foreach ($item in $all) {
            if ($ids.Contains([int]$item.ParentProcessId) -and $ids.Add([int]$item.ProcessId)) { $added = $true }
        }
    } while ($added)
    return @($all | Where-Object { $ids.Contains([int]$_.ProcessId) })
}

function Get-Role($ProcessRow, [int]$RootPid) {
    if ([int]$ProcessRow.ProcessId -eq $RootPid) { return "main" }
    if ($ProcessRow.Name -like "CSharpAiCli.AppHost*") { return "apphost" }
    $command = [string]$ProcessRow.CommandLine
    if ($command -match '--type=renderer') { return "renderer" }
    if ($command -match '--type=gpu-process') { return "gpu" }
    if ($command -match '--type=utility') { return "utility" }
    return "electron-child"
}

function Get-Sample([int]$RootPid, [int64]$ElapsedMilliseconds) {
    $rows = foreach ($item in @(Get-ProcessTree $RootPid)) {
        $process = Get-Process -Id ([int]$item.ProcessId) -ErrorAction SilentlyContinue
        if ($null -ne $process) {
            [PSCustomObject]@{
                pid = [int]$item.ProcessId
                role = Get-Role $item $RootPid
                workingSetBytes = [int64]$process.WorkingSet64
                privateBytes = [int64]$process.PrivateMemorySize64
            }
        }
    }
    $byRole = @($rows | Group-Object role | ForEach-Object {
        [PSCustomObject]@{
            role = $_.Name
            processCount = $_.Count
            workingSetBytes = [int64](($_.Group | Measure-Object workingSetBytes -Sum).Sum)
            privateBytes = [int64](($_.Group | Measure-Object privateBytes -Sum).Sum)
        }
    } | Sort-Object role)
    return [PSCustomObject]@{
        elapsedMilliseconds = $ElapsedMilliseconds
        processCount = @($rows).Count
        workingSetBytes = [int64](($rows | Measure-Object workingSetBytes -Sum).Sum)
        privateBytes = [int64](($rows | Measure-Object privateBytes -Sum).Sum)
        roles = $byRole
    }
}

$sourceRevision = (& git -C $repoRoot rev-parse --verify HEAD 2>&1 | Out-String).Trim().ToLowerInvariant()
$sourceDirty = -not [string]::IsNullOrWhiteSpace((& git -C $repoRoot status --porcelain=v1 --untracked-files=all 2>&1 | Out-String).Trim())
$packageFiles = @(Get-ChildItem -LiteralPath $resolvedPackage -Recurse -File)
$runEvidence = @()
$tempParent = Join-Path $artifactsRoot ".desktop-performance-temp"
New-Item -ItemType Directory -Path $tempParent -Force | Out-Null

for ($run = 1; $run -le $Runs; $run++) {
    $profileRoot = Join-Path $tempParent ([Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $profileRoot | Out-Null
    $previousAppData = $env:APPDATA
    $previousLocalAppData = $env:LOCALAPPDATA
    $previousAutoExit = $env:CAICLI_DESKTOP_AUTO_EXIT_MS
    $autoExitMilliseconds = $ReadySampleMilliseconds + ($IdleSeconds * 1000) + 10000
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $samples = @()
    $ownedIds = @()
    $desktopProcess = $null
    $runFailure = $null
    $cleanupFailures = @()
    try {
        $env:APPDATA = Join-Path $profileRoot "appdata"
        $env:LOCALAPPDATA = Join-Path $profileRoot "localappdata"
        $env:CAICLI_DESKTOP_AUTO_EXIT_MS = $autoExitMilliseconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $desktopProcess = Start-Process -FilePath $desktopExe -PassThru -WindowStyle Hidden
        Start-Sleep -Milliseconds $ReadySampleMilliseconds
        if ($desktopProcess.HasExited) { throw "Desktop exited before the ready sample on run $run." }
        $samples += Get-Sample $desktopProcess.Id $stopwatch.ElapsedMilliseconds
        $iterations = [Math]::Floor($IdleSeconds / $SampleIntervalSeconds)
        for ($sample = 0; $sample -lt $iterations; $sample++) {
            Start-Sleep -Seconds $SampleIntervalSeconds
            if ($desktopProcess.HasExited) { throw "Desktop exited before idle sampling completed on run $run." }
            $samples += Get-Sample $desktopProcess.Id $stopwatch.ElapsedMilliseconds
        }
        $ownedIds = @((Get-ProcessTree $desktopProcess.Id) | ForEach-Object { [int]$_.ProcessId })
        Wait-Process -Id $desktopProcess.Id -Timeout ([Math]::Ceiling($autoExitMilliseconds / 1000) + 10)
    }
    catch {
        $runFailure = $_.Exception.Message
    }
    finally {
        if ($null -ne $desktopProcess -and -not $desktopProcess.HasExited) {
            $ownedIds = @((Get-ProcessTree $desktopProcess.Id) | ForEach-Object { [int]$_.ProcessId })
            foreach ($ownedId in @($ownedIds | Sort-Object -Descending)) {
                Stop-Process -Id $ownedId -Force -ErrorAction SilentlyContinue
            }
        }
        $stopwatch.Stop()
        $env:APPDATA = $previousAppData
        $env:LOCALAPPDATA = $previousLocalAppData
        $env:CAICLI_DESKTOP_AUTO_EXIT_MS = $previousAutoExit
    }
    Start-Sleep -Milliseconds 500
    $remaining = @($ownedIds | Where-Object { $null -ne (Get-Process -Id $_ -ErrorAction SilentlyContinue) })
    if ($remaining.Count -ne 0) { $cleanupFailures += "Performance run left $($remaining.Count) owned process(es) behind." }
    try { Remove-Item -LiteralPath $profileRoot -Recurse -Force }
    catch { $cleanupFailures += "Performance run temp profile cleanup failed." }
    $tempDelta = if (Test-Path -LiteralPath $profileRoot) { 1 } else { 0 }
    if ($tempDelta -ne 0) { $cleanupFailures += "Performance run temp profile could not be released." }
    $safeFailure = @(@($runFailure) + $cleanupFailures | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        ([string]$_).Replace($repoRoot, "[repository]").Replace($profileRoot, "[profile]")
    }) -join "; "
    $runEvidence += [PSCustomObject]@{
        run = $run
        status = if ([string]::IsNullOrWhiteSpace($safeFailure)) { "Passed" } else { "Failed" }
        lifecycleMilliseconds = $stopwatch.ElapsedMilliseconds
        processDelta = $remaining.Count
        tempDelta = $tempDelta
        failure = if ([string]::IsNullOrWhiteSpace($safeFailure)) { $null } else { $safeFailure }
        samples = $samples
    }
}

$allRunsPassed = @($runEvidence | Where-Object { $_.status -ne "Passed" }).Count -eq 0
$result = [ordered]@{
    schemaVersion = 2
    status = if (-not $allRunsPassed) { "Failed" } elseif ($sourceDirty) { "Measured" } else { "Passed" }
    workload = "week77-cold-start-idle-v2"
    sourceRevision = $sourceRevision
    appHostSha256 = (Get-FileHash -LiteralPath $appHost -Algorithm SHA256).Hash
    source = [ordered]@{ revision = $sourceRevision; dirty = $sourceDirty }
    package = [ordered]@{
        fileCount = $packageFiles.Count
        bytes = [int64](($packageFiles | Measure-Object Length -Sum).Sum)
        appAsarBytes = [int64](Get-Item -LiteralPath $asar).Length
        appHostBytes = [int64](Get-Item -LiteralPath $appHost).Length
        desktopSha256 = (Get-FileHash -LiteralPath $desktopExe -Algorithm SHA256).Hash
        appHostSha256 = (Get-FileHash -LiteralPath $appHost -Algorithm SHA256).Hash
    }
    settings = [ordered]@{ runs = $Runs; readySampleMilliseconds = $ReadySampleMilliseconds; idleSeconds = $IdleSeconds; sampleIntervalSeconds = $SampleIntervalSeconds }
    baselines = [ordered]@{ week66PackageBytes = 437851431; week66AppAsarBytes = 2175389; week66WorkingSetBytes = 406470656; week66PrivateBytes = 242315264 }
    processCleanupPassed = @($runEvidence | Where-Object { $_.processDelta -ne 0 -or $_.tempDelta -ne 0 }).Count -eq 0
    runs = $runEvidence
}

$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
$result | ConvertTo-Json -Depth 10
if (-not $allRunsPassed) { exit 1 }
