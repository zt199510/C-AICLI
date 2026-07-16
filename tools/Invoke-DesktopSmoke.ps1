param(
    [string]$PackageRoot,
    [switch]$WindowClose
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
$appHostExe = Join-Path $resolvedPackage "resources\apphost\CSharpAiCli.AppHost.exe"
if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) {
    throw "Packaged Desktop executable is missing."
}
if (-not (Test-Path -LiteralPath $appHostExe -PathType Leaf)) {
    throw "Packaged AppHost resource is missing."
}

$beforeAppHost = @(Get-Process -Name "CSharpAiCli.AppHost" -ErrorAction SilentlyContinue).Count
$previousSmoke = $env:CAICLI_DESKTOP_SMOKE
$previousWindowClose = $env:CAICLI_DESKTOP_CLOSE_WINDOW_MS
try {
    if ($WindowClose) {
        $env:CAICLI_DESKTOP_SMOKE = $null
        $env:CAICLI_DESKTOP_CLOSE_WINDOW_MS = "1500"
    }
    else {
        $env:CAICLI_DESKTOP_SMOKE = "1"
        $env:CAICLI_DESKTOP_CLOSE_WINDOW_MS = $null
    }

    $process = Start-Process -FilePath $desktopExe -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(30000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "Packaged Desktop smoke did not exit within 30 seconds."
    }
    if ($process.ExitCode -ne 0) {
        throw "Packaged Desktop smoke failed with exit code $($process.ExitCode)."
    }
}
finally {
    $env:CAICLI_DESKTOP_SMOKE = $previousSmoke
    $env:CAICLI_DESKTOP_CLOSE_WINDOW_MS = $previousWindowClose
}

Start-Sleep -Milliseconds 500
$afterAppHost = @(Get-Process -Name "CSharpAiCli.AppHost" -ErrorAction SilentlyContinue).Count
if ($afterAppHost -ne $beforeAppHost) {
    throw "Packaged Desktop smoke left an AppHost process behind."
}

[PSCustomObject]@{
    desktop = $desktopExe
    desktopSize = (Get-Item -LiteralPath $desktopExe).Length
    appHost = $appHostExe
    appHostSize = (Get-Item -LiteralPath $appHostExe).Length
    appHostSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $appHostExe).Hash
    mode = if ($WindowClose) { "window-close" } else { "headless" }
    orphanAppHostDelta = $afterAppHost - $beforeAppHost
} | ConvertTo-Json
