[CmdletBinding()]
param(
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repositoryRoot 'artifacts\week79-desktop-real-model'
}
$resolvedEvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$expectedParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if (-not $resolvedEvidenceRoot.StartsWith(
    $expectedParent + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceRoot must be a child of the repository artifacts directory.'
}

$expectedFiles = @(
    'manifest.json',
    'architecture.json',
    'credential-free.json',
    'real-model-readonly.json',
    'real-model-write.json',
    'recovery.json',
    'resource-summary.json',
    'final-summary.json'
)
$expectedKinds = @(
    'manifest',
    'architecture',
    'credential-free',
    'real-model-readonly',
    'real-model-write',
    'recovery',
    'resource-summary',
    'final-summary'
)
$allowedStatuses = @(
    'NotRun',
    'Running',
    'Passed',
    'Failed',
    'Skipped',
    'Unproven',
    'Blocked',
    'Superseded'
)
$forbiddenPropertyPattern = '(?i)(secret|token|credential|api.?key|raw.?prompt|raw.?response|endpoint.?query|request.?header|command.?environment|absolute.?path|raw.?diagnostic|raw.?stderr)'
$forbiddenSensitiveValuePattern = '(?i)(sk-[a-z0-9_-]{12,}|bearer\s+[a-z0-9._-]{8,})'
$absolutePathPattern = '(^|[\s"''])([a-zA-Z]:[\\/]|\\\\[^\\])'

function Test-Node {
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [object]$Value,
        [Parameter(Mandatory = $true)]
        [string]$Location
    )

    if ($null -eq $Value) {
        return
    }
    if ($Value -is [string]) {
        if ($Value -match $forbiddenSensitiveValuePattern) {
            throw "Sensitive-looking value found at $Location."
        }
        if ($Value -match $absolutePathPattern) {
            throw "Absolute or UNC path found at $Location."
        }
        return
    }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            if ([string]$key -match $forbiddenPropertyPattern) {
                throw "Forbidden evidence property '$key' found at $Location."
            }
            Test-Node -Value $Value[$key] -Location "$Location.$key"
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $index = 0
        foreach ($item in $Value) {
            Test-Node -Value $item -Location "$Location[$index]"
            $index++
        }
        return
    }
    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Name -match $forbiddenPropertyPattern) {
            throw "Forbidden evidence property '$($property.Name)' found at $Location."
        }
        Test-Node -Value $property.Value -Location "$Location.$($property.Name)"
    }
}

for ($index = 0; $index -lt $expectedFiles.Count; $index++) {
    $fileName = $expectedFiles[$index]
    $path = Join-Path $resolvedEvidenceRoot $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing expected evidence file: $fileName"
    }

    $document = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($document.schemaVersion -ne 'week79-desktop-real-model/v1') {
        throw "Unexpected schemaVersion in $fileName."
    }
    if ($document.evidenceKind -ne $expectedKinds[$index]) {
        throw "Unexpected evidenceKind in $fileName."
    }
    if ($document.sourceRevision -notmatch '^[0-9a-f]{40}$') {
        throw "Invalid sourceRevision in $fileName."
    }
    if ($document.sourceDirty -isnot [bool]) {
        throw "Invalid sourceDirty in $fileName."
    }
    if ($allowedStatuses -notcontains $document.status) {
        throw "Invalid status in $fileName."
    }
    if ([string]::IsNullOrWhiteSpace([string]$document.summary)) {
        throw "Missing summary in $fileName."
    }
    foreach ($identityName in @('packageIdentity', 'appHostIdentity')) {
        $identity = $document.$identityName
        if ($identity.state -notin @('NotBuilt', 'Observed', 'Superseded')) {
            throw "Invalid $identityName state in $fileName."
        }
        if ($null -ne $identity.sha256 -and $identity.sha256 -notmatch '^[0-9A-F]{64}$') {
            throw "Invalid $identityName SHA-256 in $fileName."
        }
    }
    if ($document.durationMilliseconds -lt 0) {
        throw "Invalid durationMilliseconds in $fileName."
    }
    foreach ($deltaName in @('process', 'temporary', 'configuration')) {
        if ($null -eq $document.cleanupDelta.$deltaName) {
            throw "Missing cleanup delta '$deltaName' in $fileName."
        }
    }
    Test-Node -Value $document -Location $fileName
}

$manifest = Get-Content -LiteralPath (Join-Path $resolvedEvidenceRoot 'manifest.json') -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($manifest.sourceBranch -ne 'week-02-cli-commands-doctor-config') {
    throw 'Week 79 evidence was initialized on the wrong branch.'
}
if ($manifest.counts.expectedEvidenceFiles -ne $expectedFiles.Count) {
    throw 'Manifest expected-evidence count is inconsistent.'
}
if ($manifest.counts.trackedDotenvFiles -ne 0) {
    throw 'A dotenv-like file is tracked by Git.'
}
if ($manifest.authorization.providerBackedScenarios -ne 'NotGranted') {
    throw 'Provider-backed authorization must start as NotGranted.'
}
if ($manifest.authorization.priorAuthorizationReusable) {
    throw 'Prior provider authorization must not be reusable.'
}
if ($manifest.authorization.ignoredDotenvRead) {
    throw 'Week 79 initialization must not read ignored dotenv files.'
}
if ($manifest.boundaries.mcpDiscoveryDefault) {
    throw 'Week 79 production MCP discovery must default to disabled.'
}
if (-not $manifest.cleanupSampling.requireZeroDelta) {
    throw 'Cleanup sampling must require zero delta.'
}

Write-Output "Week 79 evidence validation passed: $($expectedFiles.Count) files, no forbidden fields, sensitive-looking values, or rooted paths."
