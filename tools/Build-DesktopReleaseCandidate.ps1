[CmdletBinding()]
param(
    [ValidatePattern('^0\.6\.0-rc\.[1-9][0-9]*$')][string]$CandidateId = "0.6.0-rc.1",
    [string]$OutputRoot,
    [string]$AccessibilityEvidencePath,
    [string]$PerformanceEvidencePath,
    [string]$SmokeEvidencePath,
    [switch]$ValidationOnly,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$desktopRoot = Join-Path $repoRoot "apps\desktop"
$packageRoot = Join-Path $desktopRoot "out\C-AICLI Desktop-win32-x64"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $artifactsRoot $(if ($ValidationOnly) { "desktop-rc-validation" } else { "desktop-rc" })
}

function Test-PathWithin([string]$Root, [string]$Candidate) {
    $separators = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd($separators)
    $candidatePath = [System.IO.Path]::GetFullPath($Candidate).TrimEnd($separators)
    return $candidatePath.Equals($rootPath, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidatePath.StartsWith($rootPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-RelativePath([string]$Root, [string]$Path) {
    return $Path.Substring($Root.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
}

function Assert-RegularTree([string]$Root) {
    foreach ($item in @(Get-ChildItem -LiteralPath $Root -Recurse -Force)) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Release payload contains a reparse point: $(Get-RelativePath $Root $item.FullName)"
        }
        if (-not (Test-PathWithin $Root $item.FullName)) { throw "Release payload escaped its root." }
    }
}

function Get-Inventory([string]$Root) {
    Assert-RegularTree $Root
    return @([System.IO.Directory]::EnumerateFiles($Root, "*", [System.IO.SearchOption]::AllDirectories) | ForEach-Object {
        [ordered]@{
            path = Get-RelativePath $Root $_
            size = [int64](Get-Item -LiteralPath $_).Length
            sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash
        }
    } | Sort-Object { $_.path })
}

function Read-PassedEvidence([string]$Path, [string]$Kind, [string]$Revision, [string]$AppHostHash) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "$Kind evidence is required for a release candidate." }
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-PathWithin $repoRoot $resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "$Kind evidence must be an existing file inside the repository."
    }
    $value = Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
    if ([string]$value.status -ne "Passed") { throw "$Kind evidence status is not Passed." }
    if ([string]$value.sourceRevision -ne $Revision) { throw "$Kind evidence source revision does not match." }
    if ($null -ne $value.appHostSha256 -and [string]$value.appHostSha256 -ne $AppHostHash) { throw "$Kind evidence AppHost identity does not match." }
    if ($Kind -eq "Performance") {
        if ([int]$value.schemaVersion -ne 2 -or [string]$value.workload -ne "week77-performance-gate-v2") {
            throw "Performance evidence schema or workload is unsupported."
        }
        $profiles = @($value.profiles)
        if ($profiles.Count -ne 5) { throw "Performance evidence must contain five consecutive independent profiles." }
        foreach ($profile in $profiles) {
            if ([string]$profile.status -ne "Passed") { throw "Performance evidence retained a failed profile." }
            if ([double]$profile.retention.idleWorkingSetPercent -gt 15 -or [double]$profile.retention.idlePrivateBytesPercent -gt 15) {
                throw "Performance evidence exceeded the 15 percent idle retention gate."
            }
            if ([int]$profile.cleanup.processDelta -ne 0 -or [int]$profile.cleanup.tempDelta -ne 0) {
                throw "Performance evidence contains a process or temp cleanup delta."
            }
        }
    }
    if ($Kind -eq "Accessibility") {
        if ([int]$value.schemaVersion -ne 2 -or [string]$value.type -ne "week77-accessibility-automation-v1") {
            throw "Accessibility evidence schema or type is unsupported."
        }
        if ([int]$value.results.expectedCount -ne 2 -or [int]$value.results.passedCount -ne 2 -or [int]$value.results.failedCount -ne 0) {
            throw "Accessibility automation evidence requires unpacked and packaged hardening passes."
        }
        if ([int]$value.cleanup.processDelta -ne 0 -or [int]$value.cleanup.tempDelta -ne 0) {
            throw "Accessibility automation evidence contains a process or temp cleanup delta."
        }
    }
    if ($Kind -eq "Smoke") {
        if ([int]$value.schemaVersion -ne 2 -or [string]$value.type -ne "week77-packaged-smoke-v1") {
            throw "Smoke evidence schema or type is unsupported."
        }
        if ([int]$value.results.expectedCount -ne 8 -or [int]$value.results.passedCount -ne 8 -or [int]$value.results.failedCount -ne 0) {
            throw "Smoke evidence requires all eight packaged scenarios to pass."
        }
        if ([int]$value.cleanup.processDelta -ne 0 -or [int]$value.cleanup.tempDelta -ne 0) {
            throw "Smoke evidence contains a process or temp cleanup delta."
        }
    }
    return $resolved
}

$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
if (-not (Test-PathWithin $artifactsRoot $resolvedOutputRoot)) { throw "RC output must remain under artifacts." }
$sourceRevision = (& git -C $repoRoot rev-parse --verify HEAD 2>&1 | Out-String).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') { throw "RC source revision could not be resolved." }
$sourceStatus = (& git -C $repoRoot status --porcelain=v1 --untracked-files=all 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw "RC source cleanliness could not be resolved." }
$sourceDirty = -not [string]::IsNullOrWhiteSpace($sourceStatus)
if ($sourceDirty -and -not $ValidationOnly) { throw "Release candidate requires a clean Git source tree." }
if ($SkipBuild -and -not $ValidationOnly) { throw "Release candidate packaging cannot skip the source-bound build." }

$artifactName = if ($ValidationOnly) { "$CandidateId-validation" } else { $CandidateId }
$finalRoot = [System.IO.Path]::GetFullPath((Join-Path $resolvedOutputRoot $artifactName))
if (-not (Test-PathWithin $resolvedOutputRoot $finalRoot)) { throw "Candidate output escaped its root." }
if (Test-Path -LiteralPath $finalRoot) { throw "Refusing to overwrite an existing candidate directory." }
New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
$stageRoot = Join-Path $resolvedOutputRoot (".staging-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stageRoot | Out-Null

try {
    if (-not $SkipBuild) {
        Push-Location $desktopRoot
        try {
            & npm run package:dir
            if ($LASTEXITCODE -ne 0) { throw "Desktop package build failed with exit code $LASTEXITCODE." }
        }
        finally { Pop-Location }
    }

    $desktopExe = Join-Path $packageRoot "caicli-desktop.exe"
    $asarPath = Join-Path $packageRoot "resources\app.asar"
    $appHostPath = Join-Path $packageRoot "resources\apphost\CSharpAiCli.AppHost.exe"
    $appHostNotice = Join-Path $packageRoot "resources\apphost\THIRD-PARTY-NOTICES-MAGICK.NET.txt"
    foreach ($required in @($desktopExe, $asarPath, $appHostPath, $appHostNotice, (Join-Path $packageRoot "LICENSE"), (Join-Path $packageRoot "LICENSES.chromium.html"))) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required RC payload is missing: $required" }
    }

    Push-Location $desktopRoot
    try {
        $securityAuditText = (& node scripts/audit-package.mjs --package-root $packageRoot 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Packaged security audit failed: $securityAuditText" }
        $securityAudit = $securityAuditText | ConvertFrom-Json
    }
    finally { Pop-Location }

    $payloadRoot = Join-Path $stageRoot "payload\C-AICLI Desktop-win32-x64"
    New-Item -ItemType Directory -Path (Split-Path -Parent $payloadRoot) -Force | Out-Null
    Copy-Item -LiteralPath $packageRoot -Destination $payloadRoot -Recurse
    Assert-RegularTree $payloadRoot
    $inventory = Get-Inventory $payloadRoot
    $appHostHash = (Get-FileHash -LiteralPath $appHostPath -Algorithm SHA256).Hash

    if (-not $ValidationOnly) {
        $accessibilityEvidence = Read-PassedEvidence $AccessibilityEvidencePath "Accessibility" $sourceRevision $appHostHash
        $performanceEvidence = Read-PassedEvidence $PerformanceEvidencePath "Performance" $sourceRevision $appHostHash
        $smokeEvidence = Read-PassedEvidence $SmokeEvidencePath "Smoke" $sourceRevision $appHostHash
        Copy-Item -LiteralPath $accessibilityEvidence -Destination (Join-Path $stageRoot "accessibility-review.json")
        Copy-Item -LiteralPath $performanceEvidence -Destination (Join-Path $stageRoot "performance.json")
        Copy-Item -LiteralPath $smokeEvidence -Destination (Join-Path $stageRoot "smoke.json")
    } else {
        foreach ($name in @("accessibility-review.json", "performance.json", "smoke.json")) {
            [ordered]@{ schemaVersion = 1; status = "ValidationOnly"; sourceRevision = $sourceRevision; appHostSha256 = $appHostHash } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stageRoot $name) -Encoding UTF8
        }
    }

    [ordered]@{
        schemaVersion = 1
        status = "Passed"
        sourceRevision = $sourceRevision
        appHostSha256 = $appHostHash
        highRiskFindings = 0
        packageAudit = $securityAudit
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stageRoot "security-review.json") -Encoding UTF8

    $packageJson = Get-Content -Raw -LiteralPath (Join-Path $desktopRoot "package.json") | ConvertFrom-Json
    $electronPackage = Get-Content -Raw -LiteralPath (Join-Path $desktopRoot "node_modules\electron\package.json") | ConvertFrom-Json
    $dotnetPath = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) { $dotnetPath = "dotnet" }
    $sdkVersion = (& $dotnetPath --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -ne "9.0.308") { throw "RC requires .NET SDK 9.0.308." }
    $contractPath = Join-Path $repoRoot "protocol\desktop-v1\contract.json"
    $manifest = [ordered]@{
        schemaVersion = 1
        candidateId = $CandidateId
        releaseCandidate = -not [bool]$ValidationOnly
        productVersion = [string]$packageJson.version
        sourceRevision = $sourceRevision
        sourceDirty = $sourceDirty
        buildTimestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
        os = [System.Environment]::OSVersion.VersionString
        architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
        dotnetSdk = $sdkVersion
        node = (& node --version).TrimStart('v')
        npm = (& npm --version)
        electron = [string]$electronPackage.version
        appHostRuntime = "win-x64/net9.0"
        appHostSha256 = $appHostHash
        protocol = "desktop-v1"
        contractSha256 = (Get-FileHash -LiteralPath $contractPath -Algorithm SHA256).Hash
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stageRoot "release-manifest.json") -Encoding UTF8
    $inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stageRoot "payload-inventory.json") -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $desktopRoot "THIRD_PARTY_NOTICES.md") -Destination (Join-Path $stageRoot "THIRD_PARTY_NOTICES.md")
    Copy-Item -LiteralPath (Join-Path $packageRoot "LICENSE") -Destination (Join-Path $stageRoot "ELECTRON-LICENSE")
    Copy-Item -LiteralPath (Join-Path $packageRoot "LICENSES.chromium.html") -Destination (Join-Path $stageRoot "LICENSES.chromium.html")
    Copy-Item -LiteralPath $appHostNotice -Destination (Join-Path $stageRoot "THIRD-PARTY-NOTICES-MAGICK.NET.txt")

    Add-Type -AssemblyName System.IO.Compression
    $archivePath = Join-Path $stageRoot "$CandidateId-windows-x64.zip"
    $fixedTimestamp = [DateTimeOffset]::Parse("2020-01-01T00:00:00Z")
    $stream = [System.IO.File]::Open($archivePath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($file in @([System.IO.Directory]::EnumerateFiles((Join-Path $stageRoot "payload"), "*", [System.IO.SearchOption]::AllDirectories) | Sort-Object)) {
                $entry = $archive.CreateEntry((Get-RelativePath (Join-Path $stageRoot "payload") $file), [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $entryStream = $entry.Open()
                $input = [System.IO.File]::OpenRead($file)
                try { $input.CopyTo($entryStream) } finally { $input.Dispose(); $entryStream.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }

    $checksums = @(
        [ordered]@{ path = "payload/C-AICLI Desktop-win32-x64/caicli-desktop.exe"; size = [int64](Get-Item $desktopExe).Length; sha256 = (Get-FileHash $desktopExe -Algorithm SHA256).Hash },
        [ordered]@{ path = "payload/C-AICLI Desktop-win32-x64/resources/app.asar"; size = [int64](Get-Item $asarPath).Length; sha256 = (Get-FileHash $asarPath -Algorithm SHA256).Hash },
        [ordered]@{ path = "payload/C-AICLI Desktop-win32-x64/resources/apphost/CSharpAiCli.AppHost.exe"; size = [int64](Get-Item $appHostPath).Length; sha256 = $appHostHash },
        [ordered]@{ path = "THIRD_PARTY_NOTICES.md"; size = [int64](Get-Item (Join-Path $stageRoot "THIRD_PARTY_NOTICES.md")).Length; sha256 = (Get-FileHash (Join-Path $stageRoot "THIRD_PARTY_NOTICES.md") -Algorithm SHA256).Hash },
        [ordered]@{ path = "ELECTRON-LICENSE"; size = [int64](Get-Item (Join-Path $stageRoot "ELECTRON-LICENSE")).Length; sha256 = (Get-FileHash (Join-Path $stageRoot "ELECTRON-LICENSE") -Algorithm SHA256).Hash },
        [ordered]@{ path = "LICENSES.chromium.html"; size = [int64](Get-Item (Join-Path $stageRoot "LICENSES.chromium.html")).Length; sha256 = (Get-FileHash (Join-Path $stageRoot "LICENSES.chromium.html") -Algorithm SHA256).Hash },
        [ordered]@{ path = "THIRD-PARTY-NOTICES-MAGICK.NET.txt"; size = [int64](Get-Item (Join-Path $stageRoot "THIRD-PARTY-NOTICES-MAGICK.NET.txt")).Length; sha256 = (Get-FileHash (Join-Path $stageRoot "THIRD-PARTY-NOTICES-MAGICK.NET.txt") -Algorithm SHA256).Hash },
        [ordered]@{ path = (Split-Path -Leaf $archivePath); size = [int64](Get-Item $archivePath).Length; sha256 = (Get-FileHash $archivePath -Algorithm SHA256).Hash }
    )
    $checksums | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stageRoot "checksums.json") -Encoding UTF8

    foreach ($textFile in @(Get-ChildItem -LiteralPath $stageRoot -File | Where-Object { $_.Extension -in @(".json", ".md") })) {
        $content = Get-Content -Raw -LiteralPath $textFile.FullName
        if ($content.IndexOf($repoRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or $content -match 'sk-[A-Za-z0-9_-]{16,}') {
            throw "RC evidence contains a forbidden absolute path or secret sentinel: $($textFile.Name)"
        }
    }
    Move-Item -LiteralPath $stageRoot -Destination $finalRoot
    [ordered]@{ candidatePath = $finalRoot; sourceRevision = $sourceRevision; sourceDirty = $sourceDirty; releaseCandidate = -not [bool]$ValidationOnly; appHostSha256 = $appHostHash } | ConvertTo-Json
}
catch {
    if (Test-PathWithin $resolvedOutputRoot $stageRoot -and (Test-Path -LiteralPath $stageRoot)) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
    throw
}
