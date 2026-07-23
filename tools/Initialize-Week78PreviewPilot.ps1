[CmdletBinding()]
param(
    [string]$EvidenceRoot,
    [ValidatePattern('^[A-Za-z0-9._-]{1,64}$')]
    [string]$OperatorAlias = 'week78-operator'
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

$sourceRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') {
    throw "Unable to resolve the source revision."
}
$sourceBranch = (& git -C $repositoryRoot branch --show-current).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceBranch)) {
    throw "Unable to resolve the source branch."
}
$sourceDirty = @(& git -C $repositoryRoot status --porcelain --untracked-files=normal).Count -ne 0
$capturedAtUtc = [DateTime]::UtcNow.ToString('o')

New-Item -ItemType Directory -Path $resolvedEvidenceRoot -Force | Out-Null

function Write-RedactedJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $path = Join-Path $resolvedEvidenceRoot $Name
    $json = $Value | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($path, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

$manifest = [ordered]@{
    schemaVersion = 'week78-preview-pilot/v1'
    evidenceKind = 'pilot-manifest'
    capturedAtUtc = $capturedAtUtc
    sourceRevision = $sourceRevision
    status = 'NotRun'
    summary = 'Pilot boundaries are initialized; no model, MCP server, or write scenario has run.'
    executionDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
    sourceDirty = $sourceDirty
    sourceBranch = $sourceBranch
    operatorAlias = $OperatorAlias
    identities = [ordered]@{
        cli = '0.6.0 source metadata'
        desktop = '0.6.0 Blocked Preview candidate'
        appHost = 'net9.0 source-bound build'
        modelIdentifier = $null
        mcpServerIdentifier = $null
        authorizationStatus = 'NotGranted'
    }
    projects = @(
        [ordered]@{
            projectId = 'pilot-project-a'
            projectCategory = 'C#/.NET library with tests'
            baselineRevision = $sourceRevision
            disposable = $true
            workspaceLocator = 'workspaces/project-a'
            allowedRelativePaths = @()
            excludedAreas = @('release', 'security', 'protocol', 'package', 'runtime configuration')
        },
        [ordered]@{
            projectId = 'pilot-project-b'
            projectCategory = 'C#/.NET application with tests'
            baselineRevision = $sourceRevision
            disposable = $true
            workspaceLocator = 'workspaces/project-b'
            allowedRelativePaths = @(
                'src/CSharpAiCli.Core/Diagnostics/LogPathResolver.cs',
                'src/CSharpAiCli.Tests/LogPathResolverTests.cs'
            )
            excludedAreas = @('release', 'security', 'protocol', 'package', 'runtime configuration')
        },
        [ordered]@{
            projectId = 'pilot-project-recovery'
            projectCategory = 'fresh recovery copy'
            baselineRevision = $sourceRevision
            disposable = $true
            workspaceLocator = 'workspaces/project-recovery'
            allowedRelativePaths = @()
            excludedAreas = @('release', 'security', 'protocol', 'package', 'runtime configuration')
        }
    )
    scenarios = @(
        [ordered]@{
            scenarioId = 'credential-free-baseline'
            approvalMode = 'never'
            allowedTools = @()
            timeBudgetMinutes = 120
            outputBoundary = 'redacted aggregate evidence only'
            stopConditions = @('first deterministic Gate failure', 'unexpected process or temporary-root delta')
        },
        [ordered]@{
            scenarioId = 'real-model-readonly'
            approvalMode = 'never'
            allowedTools = @('workspace.read_text', 'workspace.search')
            timeBudgetMinutes = 15
            outputBoundary = 'structured status and sanitized tool-call counts only'
            stopConditions = @('write or shell request', 'path or sensitive-data disclosure', 'orphan process')
        },
        [ordered]@{
            scenarioId = 'real-mcp'
            approvalMode = 'never'
            allowedTools = @('one predeclared side-effect-free read tool')
            timeBudgetMinutes = 10
            outputBoundary = 'sanitized capability and result summary only'
            stopConditions = @('unexpected capability', 'oversize', 'timeout', 'server exit', 'malformed response')
        },
        [ordered]@{
            scenarioId = 'real-model-write'
            approvalMode = 'on-request'
            allowedTools = @('workspace.read_text', 'workspace.search', 'workspace.apply_patch', 'bounded test command')
            timeBudgetMinutes = 20
            outputBoundary = 'at most 2 changed files and 160 diff lines'
            stopConditions = @('change outside exact allowlist', 'approval mismatch', 'repeated write', 'test regression')
        },
        [ordered]@{
            scenarioId = 'recovery'
            approvalMode = 'on-request'
            allowedTools = @('workspace.read_text', 'workspace.search')
            timeBudgetMinutes = 20
            outputBoundary = 'local identity and terminal-state summary only'
            stopConditions = @('unowned process termination', 'automatic replay', 'identity reuse', 'orphan process')
        }
    )
    cleanupSampling = [ordered]@{
        processScope = 'test-owned root and descendants only'
        temporaryScope = 'scenario-owned roots only'
        workspaceScope = 'disposable pilot workspaces only'
        requireZeroDelta = $true
    }
}
Write-RedactedJson -Name 'pilot-manifest.json' -Value $manifest

$initialEvidence = @(
    @{ Name = 'credential-free-baseline.json'; Kind = 'credential-free-baseline'; Summary = 'Credential-free baseline has not run.' },
    @{ Name = 'real-model-readonly.json'; Kind = 'real-model-readonly'; Summary = 'Real-model read-only scenarios have not run; authorization has not been granted.' },
    @{ Name = 'real-model-write.json'; Kind = 'real-model-write'; Summary = 'Controlled write scenario has not run and is gated by prior safety results.' },
    @{ Name = 'real-mcp.json'; Kind = 'real-mcp'; Summary = 'Real local MCP scenario has not run; server authorization has not been granted.' },
    @{ Name = 'recovery.json'; Kind = 'recovery'; Summary = 'Crash and restart recovery scenario has not run.' },
    @{ Name = 'resource-summary.json'; Kind = 'resource-summary'; Summary = 'Resource and cleanup aggregation has not run.' },
    @{ Name = 'pilot-summary.json'; Kind = 'pilot-summary'; Summary = 'No Week 78 Preview decision has been made.' }
)

foreach ($item in $initialEvidence) {
    Write-RedactedJson -Name $item.Name -Value ([ordered]@{
        schemaVersion = 'week78-preview-pilot/v1'
        evidenceKind = $item.Kind
        capturedAtUtc = $capturedAtUtc
        sourceRevision = $sourceRevision
        status = 'NotRun'
        summary = $item.Summary
        checks = @()
        metrics = [ordered]@{}
    })
}

Write-Output "Initialized redacted Week 78 pilot evidence at artifacts/week78-preview-pilot."
