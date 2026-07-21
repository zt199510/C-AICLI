[CmdletBinding()]
param(
    [ValidateRange(1, 10)][int]$Rounds = 3,
    [ValidateRange(1, 64)][int]$FramesPerRound = 48,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$script = Join-Path $repoRoot "apps\desktop\scripts\measure-protocol.mjs"
$arguments = @($script, "--rounds", $Rounds, "--frames", $FramesPerRound)
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) { $arguments += @("--output", $OutputPath) }
& node @arguments
if ($LASTEXITCODE -ne 0) { throw "Protocol benchmark failed with exit code $LASTEXITCODE." }
