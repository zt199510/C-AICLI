[CmdletBinding()]
param(
    [string]$ExecutablePath = "",
    [string]$TempRoot = "",
    [switch]$KeepTemp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $repoRoot "artifacts\release\caicli-0.1.0-win-x64\caicli.exe"
}

if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "Release executable not found: $ExecutablePath. Run tools/Build-Release.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($TempRoot)) {
    $TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("caicli-smoke-" + [guid]::NewGuid().ToString("N"))
}

$TempRoot = [System.IO.Path]::GetFullPath($TempRoot)
$workspace = Join-Path $TempRoot "workspace"
$userProfile = Join-Path $TempRoot "user"
$outside = Join-Path $TempRoot "outside.txt"

function Invoke-CaiCli {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & $ExecutablePath @Arguments 2>&1 | Out-String
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = $output
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $output = & git @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "$Name expected git exit code 0 but got $LASTEXITCODE. Output:`n$output"
    }

    return $output
}

function Assert-ExitCode {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result,
        [Parameter(Mandatory = $true)]
        [int]$Expected,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Result.ExitCode -ne $Expected) {
        throw "$Name expected exit code $Expected but got $($Result.ExitCode). Output:`n$($Result.Output)"
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,
        [Parameter(Mandatory = $true)]
        [string]$Expected,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Text.IndexOf($Expected, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "$Name expected output to contain '$Expected'. Output:`n$Text"
    }
}

function New-ArgumentsFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Json
    )

    $path = Join-Path $TempRoot $Name
    Set-Content -LiteralPath $path -Value $Json -Encoding UTF8
    return $path
}

$oldUserProfile = $env:USERPROFILE
$oldCaiCliUserProfile = $env:CAICLI_USER_PROFILE
$oldOpenAiKey = $env:OPENAI_API_KEY
$oldOpenAiModel = $env:OPENAI_MODEL

try {
    New-Item -ItemType Directory -Path $workspace, $userProfile | Out-Null
    Set-Content -LiteralPath (Join-Path $workspace "note.txt") -Value "before" -Encoding UTF8
    Set-Content -LiteralPath $outside -Value "outside" -Encoding UTF8

    $env:USERPROFILE = $userProfile
    $env:CAICLI_USER_PROFILE = $userProfile
    Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue

    $version = Invoke-CaiCli -Arguments @("version")
    Assert-ExitCode $version 0 "version"
    Assert-Contains $version.Output "caicli 0.1.0" "version"

    $doctor = Invoke-CaiCli -Arguments @("doctor", "--workspace", $workspace)
    Assert-ExitCode $doctor 0 "doctor"
    Assert-Contains $doctor.Output "api key: missing" "doctor"

    $status = Invoke-CaiCli -Arguments @("status", "--workspace", $workspace)
    Assert-ExitCode $status 0 "status"
    Assert-Contains $status.Output "gitStatus: not a git repository" "status"

    $models = Invoke-CaiCli -Arguments @("models", "--workspace", $workspace)
    Assert-ExitCode $models 0 "models"
    Assert-Contains $models.Output "modelListApi: not called" "models"
    Assert-Contains $models.Output "apiKey: missing" "models"

    $emptyHooks = Join-Path $TempRoot "empty-hooks"
    New-Item -ItemType Directory -Path $emptyHooks -Force | Out-Null
    Invoke-Git -Name "git init" -Arguments @("-C", $workspace, "-c", "commit.gpgSign=false", "-c", "core.hooksPath=$emptyHooks", "init") | Out-Null
    Invoke-Git -Name "git config user email" -Arguments @("-C", $workspace, "config", "user.email", "smoke@example.test") | Out-Null
    Invoke-Git -Name "git config user name" -Arguments @("-C", $workspace, "config", "user.name", "CSharp AI CLI Smoke") | Out-Null
    Invoke-Git -Name "git add" -Arguments @("-C", $workspace, "-c", "commit.gpgSign=false", "-c", "core.hooksPath=$emptyHooks", "add", "note.txt") | Out-Null
    Invoke-Git -Name "git commit" -Arguments @("-C", $workspace, "-c", "commit.gpgSign=false", "-c", "core.hooksPath=$emptyHooks", "commit", "--no-gpg-sign", "--no-verify", "-m", "Initial smoke commit") | Out-Null
    Add-Content -LiteralPath (Join-Path $workspace "note.txt") -Value "changed" -Encoding UTF8

    $diff = Invoke-CaiCli -Arguments @("diff", "--workspace", $workspace)
    Assert-ExitCode $diff 0 "diff"
    Assert-Contains $diff.Output "diff --git" "diff"

    $diffStat = Invoke-CaiCli -Arguments @("diff", "--stat", "--workspace", $workspace)
    Assert-ExitCode $diffStat 0 "diff stat"
    Assert-Contains $diffStat.Output "1 file changed" "diff stat"

    $missingModel = Invoke-CaiCli -Arguments @("chat", "--workspace", $workspace, "hello")
    Assert-ExitCode $missingModel 1 "chat missing model"
    Assert-Contains $missingModel.Output "localErrorCode: missing-model" "chat missing model"

    $env:OPENAI_MODEL = "gpt-smoke"
    $missingKey = Invoke-CaiCli -Arguments @("chat", "--workspace", $workspace, "hello")
    Assert-ExitCode $missingKey 1 "chat missing key"
    Assert-Contains $missingKey.Output "localErrorCode: missing-openai-api-key" "chat missing key"
    Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue

    $patchArguments = New-ArgumentsFile "patch-arguments.json" '{"path":"note.txt","find":"before","replace":"after"}'
    $approvalDenied = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace,
        "workspace.apply_patch",
        "--arguments-file", $patchArguments
    )
    Assert-ExitCode $approvalDenied 1 "approval denied"
    Assert-Contains $approvalDenied.Output "errorCode: approval-denied" "approval denied"

    $readOutsideArguments = New-ArgumentsFile "read-outside-arguments.json" '{"path":"../outside.txt"}'
    $pathDenied = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace,
        "workspace.read_text",
        "--arguments-file", $readOutsideArguments
    )
    Assert-ExitCode $pathDenied 1 "path boundary"
    Assert-Contains $pathDenied.Output "errorCode: workspace-boundary-denied" "path boundary"

    $configDir = Join-Path $workspace ".caicli"
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $configDir "config.json") -Encoding UTF8 -Value @'
{
  "disabledTools": [ "workspace.run_shell" ]
}
'@
    $shellArguments = New-ArgumentsFile "shell-arguments.json" '{"command":"dotnet --version"}'
    $disabledTool = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace,
        "workspace.run_shell",
        "--arguments-file", $shellArguments
    )
    Assert-ExitCode $disabledTool 1 "disabled tool"
    Assert-Contains $disabledTool.Output "errorCode: unknown-tool" "disabled tool"
    Remove-Item -LiteralPath (Join-Path $configDir "config.json") -Force

    $isWindowsRuntime = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
    $timeoutCommand = if ($isWindowsRuntime) { "ping -n 3 127.0.0.1 > nul" } else { "sleep 2" }
    $timeoutArguments = New-ArgumentsFile "timeout-arguments.json" (@{
        command = $timeoutCommand
        timeoutMilliseconds = 200
    } | ConvertTo-Json -Compress)
    $timeout = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace, "--approve",
        "workspace.run_shell",
        "--arguments-file", $timeoutArguments
    )
    Assert-ExitCode $timeout 1 "shell timeout"
    Assert-Contains $timeout.Output "errorCode: shell-timeout" "shell timeout"

    $runTask = Invoke-CaiCli -Arguments @("run", "--workspace", $workspace, "--approve", "create smoke note")
    Assert-ExitCode $runTask 0 "run smoke task"
    Assert-Contains $runTask.Output "status: succeeded" "run smoke task"
    $smokeNote = Get-Content -LiteralPath (Join-Path $workspace "caicli-smoke.txt") -Raw
    Assert-Contains $smokeNote "status: completed" "run smoke task file"

    $sessionDir = Join-Path $userProfile ".caicli\sessions"
    New-Item -ItemType Directory -Path $sessionDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $sessionDir "smoke.transcript.json") -Encoding UTF8 -Value @'
{
  "schemaVersion": 1,
  "sessionName": "smoke",
  "createdAtUtc": "2024-01-01T00:00:00+00:00",
  "updatedAtUtc": "2024-01-01T00:00:00+00:00",
  "messages": [],
  "toolCalls": [],
  "errors": []
}
'@
    $export = Invoke-CaiCli -Arguments @("session", "export", "--workspace", $workspace, "smoke")
    Assert-ExitCode $export 0 "session export"
    Assert-Contains $export.Output '"sessionName": "smoke"' "session export"

    $clear = Invoke-CaiCli -Arguments @("session", "clear", "--workspace", $workspace, "smoke")
    Assert-ExitCode $clear 0 "session clear"
    Assert-Contains $clear.Output "status: cleared" "session clear"

    Write-Host "smoke tests passed"
    Write-Host "workspace: $workspace"
}
finally {
    $env:USERPROFILE = $oldUserProfile
    if ($null -eq $oldCaiCliUserProfile) {
        Remove-Item Env:CAICLI_USER_PROFILE -ErrorAction SilentlyContinue
    } else {
        $env:CAICLI_USER_PROFILE = $oldCaiCliUserProfile
    }

    if ($null -eq $oldOpenAiKey) {
        Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
    } else {
        $env:OPENAI_API_KEY = $oldOpenAiKey
    }

    if ($null -eq $oldOpenAiModel) {
        Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue
    } else {
        $env:OPENAI_MODEL = $oldOpenAiModel
    }

    if (-not $KeepTemp -and (Test-Path -LiteralPath $TempRoot)) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force
    }
}
