[CmdletBinding()]
param(
    [string]$EvidenceRoot,
    [switch]$AllowIncomplete,
    [switch]$AllowBlocked
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $artifactsRoot 'week82-desktop-preview-requalification'
}
$resolvedEvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (-not $resolvedEvidenceRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceRoot must be below the repository artifacts directory.'
}

$candidate = 'e9e062d985545377aa373767f563de6a2bb30a64'
$desktopSha = 'C736C48B23B8971ED5DAD7F53EBF7BE6CE5CDC2BA6B24CC2CCAFE3DD9064CBB0'
$desktopBytes = 222753280L
$treeSha = 'ADDFBC3114B10E31633F1E9A9500934B9B8F17BCD3222CD4D02217AA606C6E38'
$treeBytes = 464708708L
$asarSha = 'D7959F8886DAE4F1E66068D728A8EF74C19231D8ACCA68F67A603FA2CE034C04'
$asarBytes = 555012L
$appHostSha = 'DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA'
$appHostBytes = 79941168L
$schema = 'week82-desktop-preview-requalification/v1'

function Read-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing evidence: $Path" }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Assert-Cleanup($Cleanup, [string]$Label) {
    if ($null -eq $Cleanup -or [int]$Cleanup.process -ne 0 -or [int]$Cleanup.temporary -ne 0 -or [int]$Cleanup.configuration -ne 0) {
        throw "$Label contains a non-zero or missing cleanup delta."
    }
}

function Assert-Identity($Document, [string]$Label) {
    if ([string]$Document.packageIdentity.sha256 -ne $desktopSha -or [long]$Document.packageIdentity.bytes -ne $desktopBytes) {
        throw "$Label Desktop identity mismatch."
    }
    if ($null -ne $Document.packageIdentity.treeSha256 -and [string]$Document.packageIdentity.treeSha256 -ne $treeSha) {
        throw "$Label package-tree identity mismatch."
    }
    if ([string]$Document.appHostIdentity.sha256 -ne $appHostSha -or [long]$Document.appHostIdentity.bytes -ne $appHostBytes) {
        throw "$Label AppHost identity mismatch."
    }
}

function Test-ForbiddenValue([object]$Value, [string]$Location) {
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        if ($Value -match '(?i)[a-z]:[\\/]' -or
            $Value -match 'OPENAI_(?:MODEL|BASE_URL|API_KEY)' -or
            $Value -match 'sk-[A-Za-z0-9_-]{8,}') {
            throw "Forbidden sensitive or rooted string in $Location."
        }
        return
    }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            if ([string]$key -match '^(?i:rawPrompt|rawResponse|headers|environment)$') {
                throw "Forbidden raw evidence field in $Location."
            }
            Test-ForbiddenValue $Value[$key] "$Location.$key"
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $index = 0
        foreach ($item in $Value) {
            Test-ForbiddenValue $item "$Location[$index]"
            $index++
        }
        return
    }
    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Name -match '^(?i:rawPrompt|rawResponse|headers|environment)$') {
            throw "Forbidden raw evidence field in $Location."
        }
        Test-ForbiddenValue $property.Value "$Location.$($property.Name)"
    }
}

function Get-PackageInventory([string]$Root) {
    $script = @'
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const root = path.resolve(process.argv[1]);
const values = [];
function visit(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })
    .sort((left, right) => left.name.localeCompare(right.name))) {
    const resolved = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) throw new Error('Package inventory refuses symbolic links.');
    if (entry.isDirectory()) visit(resolved);
    else if (entry.isFile()) {
      const body = fs.readFileSync(resolved);
      values.push({
        path: path.relative(root, resolved).replaceAll(path.sep, '/'),
        size: body.length,
        sha256: crypto.createHash('sha256').update(body).digest('hex').toUpperCase(),
      });
    }
  }
}
visit(root);
const canonical = values.map((value) =>
  value.path + '\0' + value.size + '\0' + value.sha256 + '\n').join('');
process.stdout.write(JSON.stringify({
  fileCount: values.length,
  bytes: values.reduce((total, value) => total + value.size, 0),
  sha256: crypto.createHash('sha256').update(canonical).digest('hex').toUpperCase(),
}));
'@
    $json = (& node -e $script $Root 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Node package inventory failed." }
    return $json | ConvertFrom-Json
}

$required = @(
    'manifest.json',
    'credential-free.json',
    'provider-readonly.json',
    'provider-write.json',
    'provider-recovery.json',
    'provider-resource-profile-1.json',
    'provider-resource-profile-2.json',
    'provider-resource-profile-3.json',
    'provider-resource-profile-4.json',
    'provider-resource-profile-5.json',
    'resource-summary.json',
    'final-summary.json'
)
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedEvidenceRoot $name) -PathType Leaf)) {
        throw "Missing Week82 evidence file: $name"
    }
}

foreach ($file in @(Get-ChildItem -LiteralPath $resolvedEvidenceRoot -Recurse -File -Filter '*.json')) {
    $parsed = Read-Json $file.FullName
    Test-ForbiddenValue $parsed $file.Name
}

$manifest = Read-Json (Join-Path $resolvedEvidenceRoot 'manifest.json')
if ($manifest.schemaVersion -ne $schema -or $manifest.exactCandidateRevision -ne $candidate -or
    [bool]$manifest.sourceDirtyAtPackageBuild -or $manifest.phase0.status -ne 'Passed' -or
    $manifest.phase1.status -ne 'Passed' -or [int]$manifest.phase0.trackedDotenvCount -ne 0 -or
    [bool]$manifest.phase0.credentialValuesRead -or [bool]$manifest.phase1.credentialValuesRead) {
    throw 'Week82 manifest does not prove a clean credential-free Phase 0-1.'
}
Assert-Identity $manifest 'Week82 manifest'
Assert-Cleanup $manifest.phase1.cleanupDelta 'Week82 Phase 1'

$credentialFree = Read-Json (Join-Path $resolvedEvidenceRoot 'credential-free.json')
if ($credentialFree.schemaVersion -ne $schema -or $credentialFree.status -ne 'Passed' -or
    $credentialFree.exactCandidateRevision -ne $candidate -or [bool]$credentialFree.credentialValuesRead -or
    [bool]$credentialFree.providerExecuted) {
    throw 'Credential-free evidence is not Passed on the exact candidate.'
}
Assert-Identity $credentialFree 'Credential-free evidence'
Assert-Cleanup $credentialFree.cleanupDelta 'Credential-free evidence'
$requiredChecks = @(
    'full-dotnet', 'desktop-verify', 'dependency-audit', 'package-audit', 'unpacked-e2e',
    'packaged-e2e', 'accessibility', 'packaged-smoke', 'protocol',
    'week77-frozen-performance', 'week80-controls', 'week81-deterministic-regression', 'git-diff-check'
)
foreach ($checkName in $requiredChecks) {
    $check = @($credentialFree.checks | Where-Object name -eq $checkName)
    if ($check.Count -ne 1 -or $check[0].status -ne 'Passed') { throw "Credential-free check did not pass: $checkName" }
}
foreach ($failureName in @('dotnet-sdk-resolution', 'dotnet-solution-path', 'week80-control-overprojection', 'week80-control-callback')) {
    if (@($credentialFree.firstFailures | Where-Object name -eq $failureName).Count -ne 1) {
        throw "Credential-free evidence lost first failure: $failureName"
    }
}

$performance = Read-Json (Join-Path $resolvedEvidenceRoot 'week77-performance.json')
if ($performance.status -ne 'Passed' -or $performance.sourceRevision -ne $candidate -or $performance.source.dirty -or
    -not $performance.gates.packageGatePassed -or -not $performance.gates.profileGatePassed) {
    throw 'Week77 frozen Gate did not pass on the exact candidate.'
}
$profiles = @($performance.profiles)
if ($profiles.Count -ne 5) { throw 'Week77 frozen Gate must contain five profiles.' }
foreach ($profile in $profiles) {
    if ($profile.status -ne 'Passed' -or [double]$profile.retention.idleWorkingSetPercent -gt 15 -or
        [double]$profile.retention.idlePrivateBytesPercent -gt 15 -or [int]$profile.cleanup.processDelta -ne 0 -or
        [int]$profile.cleanup.tempDelta -ne 0) {
        throw "Week77 profile $($profile.profile) failed retention or cleanup."
    }
}

$accessibility = Read-Json (Join-Path $resolvedEvidenceRoot 'accessibility.json')
$smoke = Read-Json (Join-Path $resolvedEvidenceRoot 'packaged-smoke.json')
foreach ($entry in @($accessibility, $smoke)) {
    if ($entry.status -ne 'Passed' -or $entry.sourceRevision -ne $candidate -or $entry.source.dirty -or
        $entry.packageSha256 -ne $treeSha -or $entry.appHostSha256 -ne $appHostSha -or
        [int]$entry.cleanup.processDelta -ne 0 -or [int]$entry.cleanup.tempDelta -ne 0) {
        throw 'Accessibility or smoke evidence failed identity, status, or cleanup validation.'
    }
}
if ([int]$accessibility.results.passedCount -ne 2 -or [int]$smoke.results.passedCount -ne 8) {
    throw 'Accessibility or smoke result count mismatch.'
}

$protocol = Read-Json (Join-Path $resolvedEvidenceRoot 'protocol.json')
if ($protocol.status -ne 'Passed' -or $protocol.sourceRevision -ne $candidate -or $protocol.sourceDirty -or
    $protocol.appHostSha256 -ne $appHostSha -or @($protocol.rounds).Count -ne 3) {
    throw 'Protocol evidence failed identity or round validation.'
}
foreach ($round in @($protocol.rounds)) {
    if ([int]$round.responseCount -ne 48 -or [int]$round.processDelta -ne 0 -or [int]$round.tempDelta -ne 0) {
        throw 'Protocol round failed response or cleanup validation.'
    }
}

$controlExpectations = [ordered]@{
    C0 = $null
    C1 = [ordered]@{ notifications = 6; calls = 6; turns = 6; items = 36 }
    C2 = [ordered]@{ notifications = $null; calls = 4; turns = 1; items = 240 }
    C3 = [ordered]@{ notifications = 40; calls = 2; turns = 6; items = 36 }
    C4 = [ordered]@{ notifications = 1; calls = 1; turns = 6; items = 36 }
    C5 = [ordered]@{ notifications = 80; calls = 20; turns = 11; items = 66 }
    C6 = [ordered]@{ notifications = 0; calls = 137; turns = 11; items = 66 }
    C7 = [ordered]@{ notifications = 80; calls = 20; turns = 11; items = 66 }
}
foreach ($name in $controlExpectations.Keys) {
    $control = Read-Json (Join-Path $resolvedEvidenceRoot "week80-controls\profile-$($name.ToLowerInvariant()).json")
    if ($control.status -ne 'Passed' -or $control.productRevision -ne $candidate -or
        $control.packageIdentity.sha256 -ne $desktopSha) {
        throw "Week80 control $name failed status or identity validation."
    }
    Assert-Cleanup $control.cleanupDelta "Week80 control $name"
    $expected = $controlExpectations[$name]
    if ($null -ne $expected) {
        if ([int]$control.workloadDiagnostics.getThreadCalls -ne [int]$expected.calls -or
            [int]$control.workloadDiagnostics.turns -ne [int]$expected.turns -or
            [int]$control.workloadDiagnostics.authoritativeTimelineItems -ne [int]$expected.items) {
            throw "Week80 control $name workload count mismatch."
        }
        if ($null -ne $expected.notifications -and [int]$control.workloadDiagnostics.emittedThreadChanges -ne [int]$expected.notifications) {
            throw "Week80 control $name notification count mismatch."
        }
    }
}

$regression = Read-Json (Join-Path $resolvedEvidenceRoot 'week81-regression.json')
if ($regression.status -ne 'Passed' -or $regression.exactCandidateRevision -ne $candidate -or
    [int]$regression.tests.passed -ne 26 -or [int]$regression.tests.failed -ne 0 -or
    [bool]$regression.rawMachinePathsRetained) {
    throw 'Week81 deterministic regression evidence failed.'
}

$recoveryFirstFailure = Read-Json (Join-Path $resolvedEvidenceRoot 'provider-recovery-first-failure.json')
if ($recoveryFirstFailure.schemaVersion -ne $schema -or $recoveryFirstFailure.status -ne 'Failed' -or
    $recoveryFirstFailure.exactCandidateRevision -ne $candidate -or
    $recoveryFirstFailure.category -ne 'provider-completed-without-requested-tool' -or
    [int]$recoveryFirstFailure.observed.timelineItems -ne 3 -or
    [int]$recoveryFirstFailure.observed.toolCalls -ne 0 -or
    [int]$recoveryFirstFailure.observed.approvalRequests -ne 0 -or
    [bool]$recoveryFirstFailure.observed.appHostCrashExecuted -or
    [bool]$recoveryFirstFailure.observed.runtimeRestartExecuted -or
    [bool]$recoveryFirstFailure.observed.writeExecuted) {
    throw 'Week82 recovery first failure was not preserved accurately.'
}
Assert-Identity $recoveryFirstFailure 'Week82 recovery first failure'
Assert-Cleanup $recoveryFirstFailure.cleanupDelta 'Week82 recovery first failure'

$packageRoot = Join-Path $repositoryRoot 'apps\desktop\out\C-AICLI Desktop-win32-x64'
$desktopPath = Join-Path $packageRoot 'caicli-desktop.exe'
$asarPath = Join-Path $packageRoot 'resources\app.asar'
$appHostPath = Join-Path $packageRoot 'resources\apphost\CSharpAiCli.AppHost.exe'
if ((Get-FileHash -LiteralPath $desktopPath -Algorithm SHA256).Hash -ne $desktopSha -or
    (Get-Item -LiteralPath $desktopPath).Length -ne $desktopBytes -or
    (Get-FileHash -LiteralPath $asarPath -Algorithm SHA256).Hash -ne $asarSha -or
    (Get-Item -LiteralPath $asarPath).Length -ne $asarBytes -or
    (Get-FileHash -LiteralPath $appHostPath -Algorithm SHA256).Hash -ne $appHostSha -or
    (Get-Item -LiteralPath $appHostPath).Length -ne $appHostBytes) {
    throw 'Current rebuilt package identity mismatch.'
}
$inventory = Get-PackageInventory $packageRoot
if ([int]$inventory.fileCount -ne 78 -or [long]$inventory.bytes -ne $treeBytes -or $inventory.sha256 -ne $treeSha) {
    throw 'Current rebuilt package-tree identity mismatch.'
}

if ($AllowIncomplete) {
    foreach ($name in @('provider-readonly.json', 'provider-write.json', 'provider-recovery.json',
        'provider-resource-profile-1.json', 'provider-resource-profile-2.json',
        'provider-resource-profile-3.json', 'provider-resource-profile-4.json',
        'provider-resource-profile-5.json', 'resource-summary.json')) {
        $document = Read-Json (Join-Path $resolvedEvidenceRoot $name)
        if ($document.status -notin @('NotRun', 'Passed', 'Failed')) { throw "Unexpected incomplete status in $name" }
    }
    $final = Read-Json (Join-Path $resolvedEvidenceRoot 'final-summary.json')
    if ($final.status -ne 'InProgress' -or $final.decision -ne 'Pending') {
        throw 'Incomplete Week82 final summary must remain InProgress/Pending.'
    }
    Write-Output 'Week82 evidence validation passed in incomplete mode: Phase 0-1, candidate identity, regressions, first failures, privacy, and cleanup are consistent.'
    return
}

if ($AllowBlocked) {
    $readOnly = Read-Json (Join-Path $resolvedEvidenceRoot 'provider-readonly.json')
    if ($readOnly.schemaVersion -ne $schema -or $readOnly.status -ne 'Passed' -or
        $readOnly.exactCandidateRevision -ne $candidate -or
        [int]$readOnly.counts.providerTurns -ne 1 -or [int]$readOnly.counts.readCalls -ne 1 -or
        [int]$readOnly.counts.approvalRequests -ne 0 -or [int]$readOnly.counts.writeCalls -ne 0 -or
        [int]$readOnly.counts.shellCalls -ne 0 -or [int]$readOnly.counts.changedFiles -ne 0) {
        throw 'Blocked Week82 read-only evidence is not a valid Passed result.'
    }
    Assert-Identity $readOnly 'Blocked Week82 read-only evidence'
    Assert-Cleanup $readOnly.cleanupDelta 'Blocked Week82 read-only evidence'
    $recovery = Read-Json (Join-Path $resolvedEvidenceRoot 'provider-recovery.json')
    if ($recovery.schemaVersion -ne $schema -or $recovery.status -ne 'Failed' -or
        $recovery.exactCandidateRevision -ne $candidate -or
        $recovery.category -ne 'renderer-approval-projection-stale' -or
        $recovery.observed.durableThreadStatus -ne 'waiting-for-approval' -or
        [int]$recovery.observed.durableToolStarted -ne 1 -or
        [int]$recovery.observed.durableApprovalRequests -ne 1 -or
        [int]$recovery.observed.rendererLoadedItems -ne 1 -or
        [int]$recovery.observed.rendererApprovalCards -ne 0 -or
        [bool]$recovery.observed.appHostCrashExecuted -or [bool]$recovery.observed.writeExecuted) {
        throw 'Blocked Week82 recovery evidence does not preserve the stale approval projection failure.'
    }
    Assert-Identity $recovery 'Blocked Week82 recovery evidence'
    Assert-Cleanup $recovery.cleanupDelta 'Blocked Week82 recovery evidence'
    $write = Read-Json (Join-Path $resolvedEvidenceRoot 'provider-write.json')
    $resourceSummary = Read-Json (Join-Path $resolvedEvidenceRoot 'resource-summary.json')
    if ($write.status -ne 'NotRun' -or $resourceSummary.status -ne 'NotRun') {
        throw 'Blocked Week82 must leave downstream write and resource Gates NotRun.'
    }
    for ($index = 1; $index -le 5; $index++) {
        $profile = Read-Json (Join-Path $resolvedEvidenceRoot "provider-resource-profile-$index.json")
        if ($profile.status -ne 'NotRun' -or $profile.authorization -ne 'GrantedNotRunDueToRecoveryGate') {
            throw "Blocked Week82 resource profile $index has an invalid stopped state."
        }
    }
    $final = Read-Json (Join-Path $resolvedEvidenceRoot 'final-summary.json')
    if ($final.schemaVersion -ne $schema -or $final.status -ne 'Blocked' -or
        $final.decision -ne 'Blocked' -or [int]$final.openP0 -ne 0 -or [int]$final.openP1 -ne 1 -or
        $final.exactCandidateRevision -ne $candidate -or $final.blockingGate -ne 'W82-G4') {
        throw 'Blocked Week82 final summary is inconsistent.'
    }
    Assert-Identity $final 'Blocked Week82 final summary'
    Assert-Cleanup $final.cleanupDelta 'Blocked Week82 final summary'
    $reviewPath = Join-Path $repositoryRoot 'docs_md\weekly\82_week_review.md'
    $handoffPath = Join-Path $resolvedEvidenceRoot 'week83-handoff.json'
    if (-not (Test-Path -LiteralPath $reviewPath -PathType Leaf) -or -not (Test-Path -LiteralPath $handoffPath -PathType Leaf)) {
        throw 'Blocked Week82 review or Week83 handoff is missing.'
    }
    $review = Get-Content -LiteralPath $reviewPath -Raw -Encoding UTF8
    if (-not $review.Contains('Blocked') -or -not $review.Contains($candidate) -or
        -not $review.Contains('renderer-approval-projection-stale') -or
        -not $review.Contains('0.6.0 formal release')) {
        throw 'Blocked Week82 review does not preserve the bounded decision.'
    }
    $handoff = Read-Json $handoffPath
    if ($handoff.status -ne 'Blocked' -or $handoff.exactCandidateRevision -ne $candidate -or
        $handoff.blockingGate -ne 'W82-G4') {
        throw 'Blocked Week83 handoff is inconsistent.'
    }
    Write-Output 'Week82 blocked evidence validation passed: read-only passed, recovery stale projection preserved, downstream provider Gates stopped, privacy and cleanup consistent.'
    return
}

foreach ($name in @('provider-readonly.json', 'provider-write.json', 'provider-recovery.json', 'resource-summary.json')) {
    $document = Read-Json (Join-Path $resolvedEvidenceRoot $name)
    if ($document.schemaVersion -ne $schema -or $document.status -ne 'Passed' -or
        $document.exactCandidateRevision -ne $candidate) {
        throw "Final provider evidence did not pass: $name"
    }
    Assert-Identity $document $name
    Assert-Cleanup $document.cleanupDelta $name
}
$readOnly = Read-Json (Join-Path $resolvedEvidenceRoot 'provider-readonly.json')
if ([int]$readOnly.counts.providerTurns -ne 1 -or [int]$readOnly.counts.readCalls -ne 1 -or
    [int]$readOnly.counts.approvalRequests -ne 0 -or [int]$readOnly.counts.writeCalls -ne 0 -or
    [int]$readOnly.counts.shellCalls -ne 0 -or [int]$readOnly.counts.changedFiles -ne 0) {
    throw 'Provider read-only evidence violated its exact boundary.'
}
$write = Read-Json (Join-Path $resolvedEvidenceRoot 'provider-write.json')
foreach ($check in @('deterministicFailingBaseline', 'exactlyOnePatch', 'exactlyOneTargetTestCommand',
    'distinctDurableApprovals', 'independentTargetTestPassed', 'timelineContiguous',
    'timelineItemIdsUnique', 'resyncBounded', 'domAndListenersBounded', 'projectDiscarded')) {
    if (-not [bool]$write.checks.$check) { throw "Provider write check failed: $check" }
}
if ([int]$write.counts.patchCalls -ne 1 -or [int]$write.counts.shellCalls -ne 1 -or
    [int]$write.counts.approvalRequests -ne 2 -or [int]$write.counts.approvalResolutions -ne 2 -or
    [int]$write.counts.changedFiles -gt 2 -or [int]$write.counts.changedLines -gt 160) {
    throw 'Provider write evidence violated tool, approval, file, or diff bounds.'
}
$recovery = Read-Json (Join-Path $resolvedEvidenceRoot 'provider-recovery.json')
foreach ($check in @('ownedAppHostCrashOnly', 'noAutomaticRestartOrReplay', 'explicitRestart',
    'oldAttemptInterrupted', 'newAttemptCanceledBeforeApproval', 'turnIdentitySeparated',
    'approvalIdentitySeparated', 'modelIdentitySeparated', 'toolIdentitySeparated',
    'timelineContiguous', 'timelineItemIdsUnique', 'resyncBounded', 'domAndListenersBounded',
    'diskUnchanged', 'reviewClean')) {
    if (-not [bool]$recovery.checks.$check) { throw "Provider recovery check failed: $check" }
}
if ([int]$recovery.counts.turns -ne 2 -or [int]$recovery.counts.appHostCrashes -ne 1 -or
    [int]$recovery.counts.runtimeRestarts -ne 1 -or [int]$recovery.counts.approvalRequests -ne 2 -or
    [int]$recovery.counts.approvalResolutions -ne 0 -or [int]$recovery.counts.changedFiles -ne 0) {
    throw 'Provider recovery evidence violated crash, replay, approval, or disk bounds.'
}
for ($index = 1; $index -le 5; $index++) {
    $profile = Read-Json (Join-Path $resolvedEvidenceRoot "provider-resource-profile-$index.json")
    if ($profile.schemaVersion -ne $schema -or $profile.status -ne 'Passed' -or
        $profile.exactCandidateRevision -ne $candidate -or [int]$profile.profile -ne $index -or
        [double]$profile.retention.workingSetPercent -gt 15 -or
        [double]$profile.retention.privateBytesPercent -gt 15 -or
        [int]$profile.counts.providerTurns -ne 6 -or [int]$profile.counts.readCalls -ne 6 -or
        [int]$profile.settings.workers -ne 1 -or [int]$profile.settings.retries -ne 0 -or
        [int]$profile.settings.warmWindowSeconds -ne 30 -or [int]$profile.settings.postWindowSeconds -ne 30 -or
        [double]$profile.retention.jsHeapUsedPercent -gt 15 -or [bool]$profile.sustainedMonotonicGrowth -or
        -not [bool]$profile.rendererBounds.resyncWithinBound -or
        -not [bool]$profile.rendererBounds.visibleTimelineCardsWithinLimit -or
        -not [bool]$profile.rendererBounds.nodesWithinLimit -or
        -not [bool]$profile.rendererBounds.documentsWithinLimit -or
        -not [bool]$profile.rendererBounds.listenersWithinLimit -or
        [int]$profile.counts.approvalRequests -ne 0 -or [int]$profile.counts.writeCalls -ne 0 -or
        [int]$profile.counts.shellCalls -ne 0 -or [int]$profile.counts.changedFiles -ne 0) {
        throw "Provider resource profile $index failed settings, counts, or retention validation."
    }
    Assert-Identity $profile "Provider resource profile $index"
    Assert-Cleanup $profile.cleanupDelta "Provider resource profile $index"
}
$resourceSummary = Read-Json (Join-Path $resolvedEvidenceRoot 'resource-summary.json')
if ([int]$resourceSummary.counts.expectedProfiles -ne 5 -or [int]$resourceSummary.counts.passedProfiles -ne 5 -or
    [int]$resourceSummary.counts.providerTurns -ne 30 -or [int]$resourceSummary.counts.readCalls -ne 30 -or
    [int]$resourceSummary.settings.workers -ne 1 -or [int]$resourceSummary.settings.retries -ne 0 -or
    [int]$resourceSummary.settings.warmWindowSeconds -ne 30 -or [int]$resourceSummary.settings.postWindowSeconds -ne 30 -or
    [bool]$resourceSummary.settings.forcedGc -or [bool]$resourceSummary.settings.rendererReloadUsedForGate) {
    throw 'Provider resource summary violated the frozen five-profile Gate.'
}

$final = Read-Json (Join-Path $resolvedEvidenceRoot 'final-summary.json')
if ($final.schemaVersion -ne $schema -or $final.status -ne 'Preview Ready' -or
    $final.decision -ne 'Preview Ready' -or [int]$final.openP0 -ne 0 -or [int]$final.openP1 -ne 0 -or
    $final.exactCandidateRevision -ne $candidate) {
    throw 'Final Week82 decision is not a zero-defect Preview Ready decision.'
}
$reviewPath = Join-Path $repositoryRoot 'docs_md\weekly\82_week_review.md'
$handoffPath = Join-Path $resolvedEvidenceRoot 'week83-handoff.json'
if (-not (Test-Path -LiteralPath $reviewPath -PathType Leaf) -or -not (Test-Path -LiteralPath $handoffPath -PathType Leaf)) {
    throw 'Final Week82 review or Week83 handoff is missing.'
}
$review = Get-Content -LiteralPath $reviewPath -Raw -Encoding UTF8
if (-not $review.Contains('Preview Ready') -or -not $review.Contains($candidate) -or
    -not $review.Contains('0.6.0 formal release') -or -not $review.Contains('Blocked')) {
    throw 'Week82 review does not preserve the bounded Preview/formal-release decision.'
}
$handoff = Read-Json $handoffPath
if ($handoff.status -ne 'Preview Ready' -or $handoff.exactCandidateRevision -ne $candidate) {
    throw 'Week83 handoff is not bound to the Preview Ready candidate.'
}

Write-Output 'Week82 evidence validation passed: all requalification Gates, identities, provider profiles, privacy, cleanup, review, and handoff are consistent.'
