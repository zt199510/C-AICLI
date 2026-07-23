[CmdletBinding()]
param(
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repositoryRoot 'artifacts\week78-preview-pilot'
}
$resolvedEvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$expectedParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if (-not $resolvedEvidenceRoot.StartsWith($expectedParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "EvidenceRoot must be a child of the repository artifacts directory."
}

$expectedFiles = @(
    'pilot-manifest.json',
    'credential-free-baseline.json',
    'real-model-readonly.json',
    'real-model-write.json',
    'real-mcp.json',
    'recovery.json',
    'resource-summary.json',
    'pilot-summary.json'
)
$allowedStatuses = @('NotRun', 'Running', 'Passed', 'Failed', 'Skipped', 'Unproven', 'Blocked')
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

foreach ($fileName in $expectedFiles) {
    $path = Join-Path $resolvedEvidenceRoot $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing expected evidence file: $fileName"
    }
    $document = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($document.schemaVersion -ne 'week78-preview-pilot/v1') {
        throw "Unexpected schemaVersion in $fileName."
    }
    if ($document.sourceRevision -notmatch '^[0-9a-f]{40}$') {
        throw "Invalid sourceRevision in $fileName."
    }
    if ($allowedStatuses -notcontains $document.status) {
        throw "Invalid status in $fileName."
    }
    if ([string]::IsNullOrWhiteSpace([string]$document.summary)) {
        throw "Missing summary in $fileName."
    }
    Test-Node -Value $document -Location $fileName
}

$manifestPath = Join-Path $resolvedEvidenceRoot 'pilot-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.evidenceKind -ne 'pilot-manifest') {
    throw "pilot-manifest.json has the wrong evidenceKind."
}
if ($manifest.projects.Count -lt 2) {
    throw "At least two disposable project entries are required."
}
$projectIds = @{}
foreach ($project in $manifest.projects) {
    if (-not $project.disposable) {
        throw "Project '$($project.projectId)' is not disposable."
    }
    if ($projectIds.ContainsKey($project.projectId)) {
        throw "Duplicate projectId '$($project.projectId)'."
    }
    $projectIds[$project.projectId] = $true
    if ([IO.Path]::IsPathRooted([string]$project.workspaceLocator)) {
        throw "Project '$($project.projectId)' uses a rooted workspace locator."
    }
    foreach ($relativePath in $project.allowedRelativePaths) {
        if ([IO.Path]::IsPathRooted([string]$relativePath) -or [string]$relativePath -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "Project '$($project.projectId)' contains an unsafe allowed path."
        }
    }
}
if ($manifest.scenarios.Count -lt 5) {
    throw "All baseline, read-only, MCP, write, and recovery scenarios must be declared."
}
if (-not $manifest.cleanupSampling.requireZeroDelta) {
    throw "Cleanup sampling must require zero delta."
}

Write-Output "Week 78 pilot evidence validation passed: $($expectedFiles.Count) files, $($manifest.projects.Count) disposable projects, no forbidden fields or path values."
