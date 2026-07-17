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
$appHost = Join-Path $resolvedPackage "resources\apphost\CSharpAiCli.AppHost.exe"
$asar = Join-Path $resolvedPackage "resources\app.asar"
$runner = Join-Path $desktopRoot "scripts\invoke-composer-smoke.mjs"
foreach ($required in @($appHost, $asar, $runner)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Composer smoke input is missing: $required" }
}

Push-Location $desktopRoot
try {
    if (-not $SkipDesktopVerify) {
        & npm run verify
        if ($LASTEXITCODE -ne 0) { throw "Desktop verification failed with exit code $LASTEXITCODE." }
    }
    $evidence = & node $runner $appHost
    if ($LASTEXITCODE -ne 0) { throw "Packaged AppHost composer smoke failed with exit code $LASTEXITCODE." }
    $inventory = & npx --no-install asar list $asar
    if ($LASTEXITCODE -ne 0) { throw "app.asar inventory failed." }
}
finally { Pop-Location }

$forbidden = @($inventory | Where-Object { $_ -match '(^|[\\/])(e2e|test-results|playwright-report)([\\/]|$)|playwright\.config|\.map$' })
if ($forbidden.Count -ne 0) { throw "Production app.asar contains test or source-map material." }
$result = $evidence | ConvertFrom-Json
$result | Add-Member -NotePropertyName appAsarBytes -NotePropertyValue (Get-Item -LiteralPath $asar).Length
$result | ConvertTo-Json
