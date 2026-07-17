param(
    [string]$PackageRoot,
    [switch]$WindowClose,
    [switch]$CrashRestart,
    [switch]$CaptureShell
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
$selectedModes = @($WindowClose.IsPresent, $CrashRestart.IsPresent, $CaptureShell.IsPresent) | Where-Object { $_ }
if ($selectedModes.Count -gt 1) {
    throw "Select only one Desktop smoke mode."
}

$desktopExe = Join-Path $resolvedPackage "caicli-desktop.exe"
$appHostExe = Join-Path $resolvedPackage "resources\apphost\CSharpAiCli.AppHost.exe"
if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) { throw "Packaged Desktop executable is missing." }
if (-not (Test-Path -LiteralPath $appHostExe -PathType Leaf)) { throw "Packaged AppHost resource is missing." }

$beforeDesktop = @(Get-Process -Name "caicli-desktop" -ErrorAction SilentlyContinue).Count
$beforeAppHost = @(Get-Process -Name "CSharpAiCli.AppHost" -ErrorAction SilentlyContinue).Count
$savedEnvironment = @{
    smoke = $env:CAICLI_DESKTOP_SMOKE
    close = $env:CAICLI_DESKTOP_CLOSE_WINDOW_MS
    crash = $env:CAICLI_DESKTOP_CRASH_RESTART
    path = $env:CAICLI_DESKTOP_CAPTURE_PATH
    width = $env:CAICLI_DESKTOP_CAPTURE_WIDTH
    height = $env:CAICLI_DESKTOP_CAPTURE_HEIGHT
}
$captures = @()

function Start-OwnedDesktop([string]$capturePath, [int]$width, [int]$height) {
    $env:CAICLI_DESKTOP_SMOKE = if (-not $WindowClose -and -not $CrashRestart -and -not $CaptureShell) { "1" } else { $null }
    $env:CAICLI_DESKTOP_CLOSE_WINDOW_MS = if ($WindowClose) { "1500" } else { $null }
    $env:CAICLI_DESKTOP_CRASH_RESTART = if ($CrashRestart) { "1" } else { $null }
    $env:CAICLI_DESKTOP_CAPTURE_PATH = if ($capturePath) { $capturePath } else { $null }
    $env:CAICLI_DESKTOP_CAPTURE_WIDTH = if ($capturePath) { $width.ToString([Globalization.CultureInfo]::InvariantCulture) } else { $null }
    $env:CAICLI_DESKTOP_CAPTURE_HEIGHT = if ($capturePath) { $height.ToString([Globalization.CultureInfo]::InvariantCulture) } else { $null }

    $process = Start-Process -FilePath $desktopExe -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(30000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "Packaged Desktop smoke did not exit within 30 seconds."
    }
    if ($process.ExitCode -ne 0) { throw "Packaged Desktop smoke failed with exit code $($process.ExitCode)." }
}

try {
    if ($CaptureShell) {
        $artifactRoot = Join-Path $repoRoot "artifacts\week72-composer"
        [System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
        foreach ($size in @(@(1920, 1080), @(1440, 900), @(1280, 720), @(760, 560))) {
            $capturePath = Join-Path $artifactRoot ("shell-{0}x{1}.png" -f $size[0], $size[1])
            Start-OwnedDesktop $capturePath $size[0] $size[1]
            if (-not (Test-Path -LiteralPath $capturePath -PathType Leaf)) { throw "Shell capture is missing: $capturePath" }
            $bytes = [System.IO.File]::ReadAllBytes($capturePath)
            if ($bytes.Length -lt 1024) { throw "Shell capture is unexpectedly small: $capturePath" }
            $actualWidth = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 16))
            $actualHeight = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 20))
            if ($actualWidth -ne $size[0] -or $actualHeight -ne $size[1]) {
                throw "Shell capture dimensions are invalid: $actualWidth x $actualHeight"
            }
            $captures += $capturePath
        }
    }
    else { Start-OwnedDesktop "" 0 0 }
}
finally {
    $env:CAICLI_DESKTOP_SMOKE = $savedEnvironment.smoke
    $env:CAICLI_DESKTOP_CLOSE_WINDOW_MS = $savedEnvironment.close
    $env:CAICLI_DESKTOP_CRASH_RESTART = $savedEnvironment.crash
    $env:CAICLI_DESKTOP_CAPTURE_PATH = $savedEnvironment.path
    $env:CAICLI_DESKTOP_CAPTURE_WIDTH = $savedEnvironment.width
    $env:CAICLI_DESKTOP_CAPTURE_HEIGHT = $savedEnvironment.height
}

Start-Sleep -Milliseconds 500
$afterDesktop = @(Get-Process -Name "caicli-desktop" -ErrorAction SilentlyContinue).Count
$afterAppHost = @(Get-Process -Name "CSharpAiCli.AppHost" -ErrorAction SilentlyContinue).Count
if ($afterDesktop -ne $beforeDesktop -or $afterAppHost -ne $beforeAppHost) {
    throw "Packaged Desktop smoke left an owned process behind."
}

[PSCustomObject]@{
    desktop = $desktopExe
    desktopSize = (Get-Item -LiteralPath $desktopExe).Length
    appHost = $appHostExe
    appHostSize = (Get-Item -LiteralPath $appHostExe).Length
    appHostSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $appHostExe).Hash
    mode = if ($WindowClose) { "window-close" } elseif ($CrashRestart) { "crash-restart" } elseif ($CaptureShell) { "capture-shell" } else { "headless" }
    orphanDesktopDelta = $afterDesktop - $beforeDesktop
    orphanAppHostDelta = $afterAppHost - $beforeAppHost
    captures = $captures
} | ConvertTo-Json
