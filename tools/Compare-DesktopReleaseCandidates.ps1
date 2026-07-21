[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateA,
    [Parameter(Mandatory)][string]$CandidateB,
    [Parameter(Mandatory)][string]$SmokeEvidenceA,
    [Parameter(Mandatory)][string]$SmokeEvidenceB,
    [Parameter(Mandatory)][string]$NarratorEvidence,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $artifactsRoot "desktop-acceptance\week77-candidate-comparison.json" }

function Test-PathWithin([string]$Root, [string]$Candidate) {
    $relative = [System.IO.Path]::GetRelativePath([System.IO.Path]::GetFullPath($Root), [System.IO.Path]::GetFullPath($Candidate))
    return $relative -eq "." -or (-not $relative.StartsWith("..$([System.IO.Path]::DirectorySeparatorChar)") -and $relative -ne ".." -and -not [System.IO.Path]::IsPathRooted($relative))
}

function Resolve-ArtifactDirectory([string]$Path, [string]$Name) {
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-PathWithin $artifactsRoot $resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Container)) { throw "$Name must be a candidate directory under artifacts." }
    foreach ($item in @(Get-ChildItem -LiteralPath $resolved -Recurse -Force)) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Name contains a reparse point." }
    }
    return $resolved
}

function Read-JsonArtifact([string]$Path, [string]$Name) {
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-PathWithin $artifactsRoot $resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "$Name must be a JSON file under artifacts." }
    return Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
}

function Get-Candidate([string]$Root, [string]$Name) {
    $manifestPath = Join-Path $Root "release-manifest.json"
    $inventoryPath = Join-Path $Root "payload-inventory.json"
    $archive = @(Get-ChildItem -LiteralPath $Root -File -Filter "0.6.0-rc.*-windows-x64.zip")
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or -not (Test-Path -LiteralPath $inventoryPath -PathType Leaf) -or $archive.Count -ne 1) { throw "$Name is missing its manifest, inventory, or unique archive." }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ([string]$manifest.candidateId -ne "0.6.0-rc.1" -or [string]$manifest.productVersion -ne "0.6.0" -or -not [bool]$manifest.releaseCandidate -or [bool]$manifest.sourceDirty) { throw "$Name manifest is not a clean 0.6.0-rc.1 candidate." }
    $payloadRoot = Join-Path $Root "payload\C-AICLI Desktop-win32-x64"
    $desktopPath = Join-Path $payloadRoot "caicli-desktop.exe"
    $appHostPath = Join-Path $payloadRoot "resources\apphost\CSharpAiCli.AppHost.exe"
    $asarPath = Join-Path $payloadRoot "resources\app.asar"
    foreach ($required in @($desktopPath, $appHostPath, $asarPath)) { if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "$Name payload identity is incomplete." } }
    return [ordered]@{
        name = $Name
        root = $Root
        manifest = $manifest
        inventoryJson = (Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json | ConvertTo-Json -Depth 6 -Compress)
        inventory = @(Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json)
        archiveSha256 = (Get-FileHash -LiteralPath $archive[0].FullName -Algorithm SHA256).Hash
        archiveBytes = [int64]$archive[0].Length
        desktopSha256 = (Get-FileHash -LiteralPath $desktopPath -Algorithm SHA256).Hash
        desktopBytes = [int64](Get-Item -LiteralPath $desktopPath).Length
        appHostSha256 = (Get-FileHash -LiteralPath $appHostPath -Algorithm SHA256).Hash
        appHostBytes = [int64](Get-Item -LiteralPath $appHostPath).Length
        asarSha256 = (Get-FileHash -LiteralPath $asarPath -Algorithm SHA256).Hash
        asarBytes = [int64](Get-Item -LiteralPath $asarPath).Length
        noticesSha256 = (Get-FileHash -LiteralPath (Join-Path $Root "THIRD_PARTY_NOTICES.md") -Algorithm SHA256).Hash
        chromiumLicensesSha256 = (Get-FileHash -LiteralPath (Join-Path $Root "LICENSES.chromium.html") -Algorithm SHA256).Hash
        magickNoticeSha256 = (Get-FileHash -LiteralPath (Join-Path $Root "THIRD-PARTY-NOTICES-MAGICK.NET.txt") -Algorithm SHA256).Hash
    }
}

function Assert-Smoke($Evidence, $Candidate, [string]$Name) {
    if ([int]$Evidence.schemaVersion -ne 2 -or [string]$Evidence.type -ne "week77-packaged-smoke-v1" -or [string]$Evidence.status -ne "Passed") { throw "$Name is not Passed Week 77 packaged smoke evidence." }
    if ([string]$Evidence.sourceRevision -ne [string]$Candidate.manifest.sourceRevision -or [string]$Evidence.appHostSha256 -ne [string]$Candidate.appHostSha256 -or [string]$Evidence.packageSha256 -ne [string]$Candidate.archiveSha256) { throw "$Name identity does not match its candidate." }
    if ([int]$Evidence.results.expectedCount -ne 8 -or [int]$Evidence.results.passedCount -ne 8 -or [int]$Evidence.results.failedCount -ne 0 -or [int]$Evidence.results.skippedCount -ne 0) { throw "$Name did not pass all eight packaged scenarios." }
    if ([int]$Evidence.cleanup.processDelta -ne 0 -or [int]$Evidence.cleanup.tempDelta -ne 0) { throw "$Name contains a process or temp cleanup delta." }
}

function Assert-Narrator($Evidence, $Candidate) {
    if ([int]$Evidence.schemaVersion -ne 2 -or [string]$Evidence.type -ne "week77-narrator-manual-v1" -or [string]$Evidence.status -ne "Passed" -or -not [bool]$Evidence.manualNarratorRun) { throw "Narrator evidence is not a Passed manual run." }
    if ([string]::IsNullOrWhiteSpace([string]$Evidence.operator) -or [string]::IsNullOrWhiteSpace([string]$Evidence.environment.windowsBuild) -or [string]::IsNullOrWhiteSpace([string]$Evidence.environment.narratorVersion)) { throw "Narrator evidence is missing the human operator or Windows/Narrator version." }
    $steps = @($Evidence.steps)
    if ($steps.Count -ne 7 -or @($steps | Where-Object { [string]$_.status -ne "Passed" }).Count -ne 0) { throw "Narrator evidence requires all seven manual steps to pass." }
    if ([string]$Evidence.sourceRevision -ne [string]$Candidate.manifest.sourceRevision -or [string]$Evidence.packageSha256 -ne [string]$Candidate.archiveSha256 -or [string]$Evidence.desktopSha256 -ne [string]$Candidate.desktopSha256 -or [string]$Evidence.appHostSha256 -ne [string]$Candidate.appHostSha256) { throw "Narrator evidence identity does not match the accepted candidate." }
    if ([string]::IsNullOrWhiteSpace([string]$Evidence.declaration)) { throw "Narrator evidence requires a manual execution declaration." }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
if (-not (Test-PathWithin $artifactsRoot $resolvedOutput) -or $resolvedOutput -eq $artifactsRoot) { throw "Candidate comparison output must remain under artifacts." }
$failure = $null
$result = $null
try {
    $sourceStatus = (& git -C $repoRoot status --porcelain=v1 --untracked-files=all 2>&1 | Out-String).Trim()
    if (-not [string]::IsNullOrWhiteSpace($sourceStatus)) { throw "Candidate comparison requires the confirmed clean source revision." }
    $a = Get-Candidate (Resolve-ArtifactDirectory $CandidateA "Candidate A") "Candidate A"
    $b = Get-Candidate (Resolve-ArtifactDirectory $CandidateB "Candidate B") "Candidate B"
    foreach ($field in @("sourceRevision", "productVersion", "appHostSha256", "protocol", "contractSha256", "dotnetSdk", "node", "npm", "electron")) {
        if ([string]$a.manifest.$field -ne [string]$b.manifest.$field) { throw "Candidate manifests differ at $field." }
    }
    foreach ($field in @("inventoryJson", "archiveSha256", "archiveBytes", "desktopSha256", "desktopBytes", "appHostSha256", "appHostBytes", "asarSha256", "asarBytes", "noticesSha256", "chromiumLicensesSha256", "magickNoticeSha256")) {
        if ([string]$a.$field -ne [string]$b.$field) { throw "Candidate payloads differ at $field." }
    }
    $smokeA = Read-JsonArtifact $SmokeEvidenceA "Candidate A smoke evidence"
    $smokeB = Read-JsonArtifact $SmokeEvidenceB "Candidate B smoke evidence"
    Assert-Smoke $smokeA $a "Candidate A smoke evidence"
    Assert-Smoke $smokeB $b "Candidate B smoke evidence"
    $narrator = Read-JsonArtifact $NarratorEvidence "Narrator evidence"
    Assert-Narrator $narrator $a
    $result = [ordered]@{
        schemaVersion = 1
        status = "Passed"
        sourceRevision = [string]$a.manifest.sourceRevision
        candidateId = "0.6.0-rc.1"
        identicalPayload = $true
        expectedManifestDifference = @("buildTimestampUtc")
        inventoryFileCount = @($a.inventory).Count
        archive = [ordered]@{ bytes = $a.archiveBytes; sha256 = $a.archiveSha256 }
        desktop = [ordered]@{ bytes = $a.desktopBytes; sha256 = $a.desktopSha256 }
        appAsar = [ordered]@{ bytes = $a.asarBytes; sha256 = $a.asarSha256 }
        appHost = [ordered]@{ bytes = $a.appHostBytes; sha256 = $a.appHostSha256 }
        candidateA = [ordered]@{ smokeStatus = [string]$smokeA.status; processDelta = [int]$smokeA.cleanup.processDelta; tempDelta = [int]$smokeA.cleanup.tempDelta }
        candidateB = [ordered]@{ smokeStatus = [string]$smokeB.status; processDelta = [int]$smokeB.cleanup.processDelta; tempDelta = [int]$smokeB.cleanup.tempDelta }
        narrator = [ordered]@{ status = [string]$narrator.status; operator = [string]$narrator.operator; completedAtUtc = [string]$narrator.completedAtUtc }
    }
}
catch {
    $failure = $_.Exception.Message
    $result = [ordered]@{ schemaVersion = 1; status = "Failed"; failure = $failure }
}

New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedOutput) -Force | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
$result | ConvertTo-Json -Depth 8
if ($null -ne $failure) { throw $failure }
