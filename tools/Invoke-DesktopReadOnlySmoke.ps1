param(
    [string]$PackageRoot,
    [switch]$SkipDesktopVerify
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopRoot = Join-Path $repoRoot "apps\desktop"
if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    $PackageRoot = Join-Path $desktopRoot "out\C-AICLI Desktop-win32-x64"
}

$resolvedPackage = [System.IO.Path]::GetFullPath($PackageRoot)
$desktopOut = [System.IO.Path]::GetFullPath((Join-Path $desktopRoot "out"))
if (-not $resolvedPackage.StartsWith($desktopOut + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Desktop package must remain under apps\desktop\out."
}

$desktopExe = Join-Path $resolvedPackage "caicli-desktop.exe"
$appHostExe = Join-Path $resolvedPackage "resources\apphost\CSharpAiCli.AppHost.exe"
$asarPath = Join-Path $resolvedPackage "resources\app.asar"
foreach ($required in @($desktopExe, $appHostExe, $asarPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Read-only smoke input is missing: $required" }
}

$beforeDesktop = @(Get-Process -Name "caicli-desktop" -ErrorAction SilentlyContinue).Count
$beforeAppHost = @(Get-Process -Name "CSharpAiCli.AppHost" -ErrorAction SilentlyContinue).Count

Push-Location $desktopRoot
try {
    if (-not $SkipDesktopVerify) {
        & npm run verify
        if ($LASTEXITCODE -ne 0) { throw "Desktop verification failed with exit code $LASTEXITCODE." }
    }
    & npm run test:e2e:unpacked
    if ($LASTEXITCODE -ne 0) { throw "Unpacked read-only E2E failed with exit code $LASTEXITCODE." }
    & npm run test:e2e:packaged
    if ($LASTEXITCODE -ne 0) { throw "Packaged read-only E2E failed with exit code $LASTEXITCODE." }

    $inventory = & npx --no-install asar list $asarPath
    if ($LASTEXITCODE -ne 0) { throw "app.asar inventory failed with exit code $LASTEXITCODE." }
    $forbidden = @($inventory | Where-Object { $_ -match '(^|[\\/])(e2e|test-results|playwright-report)([\\/]|$)|playwright\.config' })
    if ($forbidden.Count -ne 0) { throw "Production app.asar contains E2E material: $($forbidden -join ', ')" }
}
finally {
    Pop-Location
}

& (Join-Path $PSScriptRoot "Invoke-DesktopSmoke.ps1") -PackageRoot $resolvedPackage -CaptureShell
if ($LASTEXITCODE -ne 0) { throw "Packaged capture smoke failed with exit code $LASTEXITCODE." }

Start-Sleep -Milliseconds 500
$afterDesktop = @(Get-Process -Name "caicli-desktop" -ErrorAction SilentlyContinue).Count
$afterAppHost = @(Get-Process -Name "CSharpAiCli.AppHost" -ErrorAction SilentlyContinue).Count
if ($afterDesktop -ne $beforeDesktop -or $afterAppHost -ne $beforeAppHost) {
    throw "Read-only Desktop smoke left an owned process behind."
}

[PSCustomObject]@{
    packageRoot = $resolvedPackage
    appAsarBytes = (Get-Item -LiteralPath $asarPath).Length
    appHostSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $appHostExe).Hash
    unpackedE2E = "passed"
    packagedE2E = "passed"
    productionFixtureMatches = 0
    orphanDesktopDelta = $afterDesktop - $beforeDesktop
    orphanAppHostDelta = $afterAppHost - $beforeAppHost
} | ConvertTo-Json
