param(
    [string]$PackageRoot,
    [int]$SampleAfterMilliseconds = 3000,
    [int]$AutoExitMilliseconds = 6000
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    $PackageRoot = Join-Path $repoRoot "apps\desktop\out\C-AICLI Desktop-win32-x64"
}

$resolvedPackage = [System.IO.Path]::GetFullPath($PackageRoot)
$desktopOut = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "apps\desktop\out"))
if (-not $resolvedPackage.StartsWith($desktopOut + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Desktop package must remain under apps\desktop\out."
}

$desktopExe = Join-Path $resolvedPackage "caicli-desktop.exe"
if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) {
    throw "Packaged Desktop executable is missing."
}

$packageFiles = @(Get-ChildItem -LiteralPath $resolvedPackage -Recurse -File)
$beforeAppHost = @(Get-Process -Name "CSharpAiCli.AppHost" -ErrorAction SilentlyContinue).Count
$previousAutoExit = $env:CAICLI_DESKTOP_AUTO_EXIT_MS
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $env:CAICLI_DESKTOP_AUTO_EXIT_MS = $AutoExitMilliseconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $desktopProcess = Start-Process -FilePath $desktopExe -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds $SampleAfterMilliseconds
    if ($desktopProcess.HasExited) {
        throw "Desktop exited before the memory sample."
    }

    $allProcesses = @(Get-CimInstance Win32_Process)
    $processIds = @([int]$desktopProcess.Id)
    do {
        $children = @($allProcesses | Where-Object {
            $processIds -contains [int]$_.ParentProcessId -and $processIds -notcontains [int]$_.ProcessId
        } | ForEach-Object { [int]$_.ProcessId })
        $newChildren = @($children | Where-Object { $processIds -notcontains $_ })
        $processIds += $newChildren
    } while ($newChildren.Count -gt 0)

    $liveProcesses = @($processIds | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    $workingSetBytes = ($liveProcesses | Measure-Object WorkingSet64 -Sum).Sum
    $privateBytes = ($liveProcesses | Measure-Object PrivateMemorySize64 -Sum).Sum
    Wait-Process -Id $desktopProcess.Id -Timeout ([Math]::Ceiling($AutoExitMilliseconds / 1000) + 10)
}
finally {
    $stopwatch.Stop()
    $env:CAICLI_DESKTOP_AUTO_EXIT_MS = $previousAutoExit
}

Start-Sleep -Milliseconds 500
$afterAppHost = @(Get-Process -Name "CSharpAiCli.AppHost" -ErrorAction SilentlyContinue).Count
if ($afterAppHost -ne $beforeAppHost) {
    throw "Desktop baseline measurement left an AppHost process behind."
}

[PSCustomObject]@{
    packageFileCount = $packageFiles.Count
    packageBytes = ($packageFiles | Measure-Object Length -Sum).Sum
    appAsarBytes = (Get-Item -LiteralPath (Join-Path $resolvedPackage "resources\app.asar")).Length
    appHostBytes = (Get-Item -LiteralPath (Join-Path $resolvedPackage "resources\apphost\CSharpAiCli.AppHost.exe")).Length
    processCountAtSample = $liveProcesses.Count
    workingSetBytesAtSample = $workingSetBytes
    privateBytesAtSample = $privateBytes
    lifecycleMilliseconds = $stopwatch.ElapsedMilliseconds
    orphanAppHostDelta = $afterAppHost - $beforeAppHost
} | ConvertTo-Json
