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
$propsPath = Join-Path $repoRoot "Directory.Build.props"

if (-not (Test-Path -LiteralPath $propsPath)) {
    throw "Version metadata not found: $propsPath"
}

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$releaseVersion = [string]$props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($releaseVersion)) {
    throw "Directory.Build.props must define a Version property."
}

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $repoRoot "artifacts\release\caicli-$releaseVersion-win-x64\caicli.exe"
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

function ConvertTo-CmdArgument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Argument
    )

    return '"' + $Argument.Replace('"', '\"') + '"'
}

function Invoke-CaiCliWithStdin {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$StandardInput
    )

    $stdinPath = Join-Path $TempRoot ("stdin-" + [guid]::NewGuid().ToString("N") + ".json")
    [System.IO.File]::WriteAllText($stdinPath, $StandardInput, [System.Text.Encoding]::ASCII)
    $argumentText = ($Arguments | ForEach-Object { ConvertTo-CmdArgument $_ }) -join ' '
    $commandLine = "type $(ConvertTo-CmdArgument $stdinPath) | $(ConvertTo-CmdArgument $ExecutablePath) $argumentText"
    $output = & cmd.exe /d /c $commandLine 2>&1 | Out-String

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

function Assert-NotContains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,
        [Parameter(Mandatory = $true)]
        [string]$Unexpected,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Text.IndexOf($Unexpected, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "$Name expected output not to contain '$Unexpected'. Output:`n$Text"
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

function Get-SmokePowerShellExecutable {
    if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
        return "powershell.exe"
    }

    return "pwsh"
}

function New-FakeMcpServerScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Set-Content -LiteralPath $Path -Encoding UTF8 -Value @'
param(
    [Parameter(Mandatory=$true)][string]$StartedPath
)

$ErrorActionPreference = 'Stop'
[System.IO.File]::WriteAllText($StartedPath, 'started')

function Get-PropertyValue {
    param($Object, [string]$Name)
    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Write-ResultResponse {
    param($Id, $Result)

    $response = [ordered]@{
        jsonrpc = '2.0'
        id = $Id
        result = $Result
    } | ConvertTo-Json -Compress -Depth 20
    [Console]::Out.WriteLine($response)
    [Console]::Out.Flush()
}

function Write-ErrorResponse {
    param($Id, [int]$Code, [string]$Message)

    $response = [ordered]@{
        jsonrpc = '2.0'
        id = $Id
        error = [ordered]@{
            code = $Code
            message = $Message
        }
    } | ConvertTo-Json -Compress -Depth 20
    [Console]::Out.WriteLine($response)
    [Console]::Out.Flush()
}

while (($line = [Console]::In.ReadLine()) -ne $null) {
    $message = $line | ConvertFrom-Json
    $method = Get-PropertyValue $message 'method'
    $id = Get-PropertyValue $message 'id'

    if ($method -eq 'initialize') {
        $params = Get-PropertyValue $message 'params'
        Write-ResultResponse $id ([ordered]@{
            protocolVersion = Get-PropertyValue $params 'protocolVersion'
            capabilities = [ordered]@{
                tools = [ordered]@{
                    listChanged = $false
                }
            }
            serverInfo = [ordered]@{
                name = 'caicli-smoke-mcp'
                version = '1.0.0'
            }
        })
        continue
    }

    if ($method -eq 'notifications/initialized') {
        continue
    }

    if ($method -eq 'tools/list') {
        Write-ResultResponse $id ([ordered]@{
            tools = @(
                [ordered]@{
                    name = 'echo'
                    description = 'Echo smoke input.'
                    inputSchema = [ordered]@{
                        type = 'object'
                        properties = [ordered]@{
                            text = [ordered]@{
                                type = 'string'
                            }
                        }
                        required = @('text')
                    }
                }
            )
        })
        continue
    }

    if ($method -eq 'tools/call') {
        $params = Get-PropertyValue $message 'params'
        $arguments = Get-PropertyValue $params 'arguments'
        $text = [string](Get-PropertyValue $arguments 'text')
        Write-ResultResponse $id ([ordered]@{
            content = @(
                [ordered]@{
                    type = 'text'
                    text = "echo: $text"
                }
            )
            structuredContent = [ordered]@{
                echoed = $text
            }
        })
        continue
    }

    Write-ErrorResponse $id -32601 'Method not found.'
}
'@
}

$oldUserProfile = $env:USERPROFILE
$oldCaiCliUserProfile = $env:CAICLI_USER_PROFILE
$oldOpenAiKey = $env:OPENAI_API_KEY
$oldOpenAiModel = $env:OPENAI_MODEL
$realModelSmokeOptIn = [System.String]::Equals($env:CAICLI_REAL_MODEL_SMOKE, "1", [System.StringComparison]::Ordinal)

try {
    New-Item -ItemType Directory -Path $workspace, $userProfile | Out-Null
    Set-Content -LiteralPath (Join-Path $workspace "note.txt") -Value "before" -Encoding UTF8
    Set-Content -LiteralPath $outside -Value "outside" -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $workspace "AGENTS.md") -Value "Prefer concise smoke output." -Encoding UTF8
    New-Item -ItemType Directory -Path (Join-Path $workspace ".caicli\automations") -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $workspace ".caicli\automations\nightly-review.json") -Value @'
{
  "schemaVersion": 1,
  "name": "nightly-review",
  "description": "Review the workspace using a schedule preview and manual trigger.",
  "trigger": {
    "type": "schedule",
    "schedule": "0 2 * * *",
    "timeZone": "UTC"
  },
  "target": {
    "type": "skill",
    "name": "review-only",
    "task": "Review @file:note.txt",
    "report": "none"
  },
  "safety": {
    "manualOnly": true,
    "allowWrites": false,
    "allowShell": false,
    "allowMcp": false
  }
}
'@ -Encoding UTF8
    New-Item -ItemType Directory -Path (Join-Path $workspace "src\app") -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $workspace "src\app\AGENTS.md") -Value "Use app-specific smoke instructions." -Encoding UTF8
    New-Item -ItemType Directory -Path (Join-Path $workspace "src\BuggyApp"), (Join-Path $workspace "tests") -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $workspace "src\BuggyApp\Calculator.txt") -Value @'
name: BuggyApp calculator fixture
expected: 41
'@ -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $workspace "tests\Verify-BuggyApp.ps1") -Value @'
$ErrorActionPreference = 'Stop'
$text = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\BuggyApp\Calculator.txt') -Raw
if ($text -notmatch 'expected:\s*42') {
    Write-Error 'BuggyApp expected value was not fixed.'
    exit 1
}

Write-Output 'bugfix verification passed'
'@ -Encoding UTF8

    $env:USERPROFILE = $userProfile
    $env:CAICLI_USER_PROFILE = $userProfile
    Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue

    $version = Invoke-CaiCli -Arguments @("version")
    Assert-ExitCode $version 0 "version"
    Assert-Contains $version.Output "caicli $releaseVersion" "version"

    $configGet = Invoke-CaiCli -Arguments @("config", "get", "--workspace", $workspace)
    Assert-ExitCode $configGet 0 "config get"
    Assert-Contains $configGet.Output "baseUrl: https://api.openai.com/v1" "config get"
    Assert-Contains $configGet.Output "approvalMode: on-request" "config get"

    $configSetBaseUrl = Invoke-CaiCli -Arguments @(
        "config", "set", "baseUrl", "https://gateway.example.test/v1",
        "--workspace", $workspace
    )
    Assert-ExitCode $configSetBaseUrl 0 "config set baseUrl"
    Assert-Contains $configSetBaseUrl.Output "status: updated" "config set baseUrl"
    Assert-Contains $configSetBaseUrl.Output "key: baseUrl" "config set baseUrl"
    Assert-Contains $configSetBaseUrl.Output "scope: user" "config set baseUrl"

    $configList = Invoke-CaiCli -Arguments @("config", "list", "--workspace", $workspace)
    Assert-ExitCode $configList 0 "config list"
    Assert-Contains $configList.Output "baseUrl: https://gateway.example.test/v1" "config list"
    Assert-Contains $configList.Output "baseUrlSource: user config" "config list"

    $configUnsetBaseUrl = Invoke-CaiCli -Arguments @("config", "unset", "baseUrl", "--workspace", $workspace)
    Assert-ExitCode $configUnsetBaseUrl 0 "config unset baseUrl"
    Assert-Contains $configUnsetBaseUrl.Output "status: updated" "config unset baseUrl"

    $doctor = Invoke-CaiCli -Arguments @("doctor", "--workspace", $workspace, "--trace")
    Assert-ExitCode $doctor 0 "doctor"
    Assert-Contains $doctor.Output "api key: missing" "doctor"
    Assert-Contains $doctor.Output "instruction source: 0:" "doctor instructions"
    Assert-Contains $doctor.Output "mcp execution policy startup risk check: enabled" "doctor mcp policy"
    Assert-NotContains $doctor.Output "command.start" "doctor trace stdout"

    $logsPath = Invoke-CaiCli -Arguments @("logs", "path", "--workspace", $workspace)
    Assert-ExitCode $logsPath 0 "logs path"
    Assert-Contains $logsPath.Output ".caicli" "logs path"

    $logsShow = Invoke-CaiCli -Arguments @("logs", "show", "--tail", "20", "--workspace", $workspace)
    Assert-ExitCode $logsShow 0 "logs show"
    Assert-Contains $logsShow.Output "command=doctor" "logs show command log"
    Assert-Contains $logsShow.Output "command.start" "logs show trace log"

    $status = Invoke-CaiCli -Arguments @("status", "--workspace", $workspace)
    Assert-ExitCode $status 0 "status"
    Assert-Contains $status.Output "gitStatus: not a git repository" "status"

    $models = Invoke-CaiCli -Arguments @("models", "--workspace", $workspace)
    Assert-ExitCode $models 0 "models"
    Assert-Contains $models.Output "modelListApi: not called" "models"
    Assert-Contains $models.Output "apiKey: missing" "models"

    $workspaceConfigDir = Join-Path $workspace ".caicli"
    New-Item -ItemType Directory -Path $workspaceConfigDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $workspaceConfigDir "config.json") -Encoding UTF8 -Value @'
{
  "workflowProfiles": {
    "smoke": {
      "description": "Smoke validation",
      "workspacePath": ".",
      "validationCommand": "dotnet test"
    }
  }
}
'@
    $workflowList = Invoke-CaiCli -Arguments @("workflow", "list", "--workspace", $workspace)
    Assert-ExitCode $workflowList 0 "workflow list"
    Assert-Contains $workflowList.Output "profile: smoke" "workflow list"

    $workflowValidate = Invoke-CaiCli -Arguments @("workflow", "validate", "smoke", "--workspace", $workspace)
    Assert-ExitCode $workflowValidate 0 "workflow validate"
    Assert-Contains $workflowValidate.Output "validationCommand: dotnet test" "workflow validate"
    Assert-Contains $workflowValidate.Output "execution: not run" "workflow validate"
    Remove-Item -LiteralPath (Join-Path $workspaceConfigDir "config.json") -Force

    $skillsList = Invoke-CaiCli -Arguments @("skills", "list", "--workspace", $workspace)
    Assert-ExitCode $skillsList 0 "skills list"
    Assert-Contains $skillsList.Output "C# AI CLI skills" "skills list"
    Assert-Contains $skillsList.Output "test-fix" "skills list"
    Assert-Contains $skillsList.Output "review-only" "skills list"

    $skillsListJson = Invoke-CaiCli -Arguments @("skills", "list", "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $skillsListJson 0 "skills list json"
    Assert-Contains $skillsListJson.Output '"type":"skills.list"' "skills list json"
    Assert-Contains $skillsListJson.Output '"name":"test-fix"' "skills list json"

    $skillsDryRun = Invoke-CaiCli -Arguments @(
        "skills", "run", "review-only", "--dry-run", "--workspace", $workspace,
        "--", "Review", "@file:note.txt"
    )
    Assert-ExitCode $skillsDryRun 0 "skills run dry-run"
    Assert-Contains $skillsDryRun.Output "C# AI CLI skill run plan" "skills run dry-run"
    Assert-Contains $skillsDryRun.Output "dryRun: true" "skills run dry-run"
    Assert-Contains $skillsDryRun.Output "skill: review-only" "skills run dry-run"
    Assert-Contains $skillsDryRun.Output "expert: reviewer" "skills run dry-run"
    Assert-Contains $skillsDryRun.Output "allowWrites=false" "skills run dry-run"

    $skillsDryRunJson = Invoke-CaiCli -Arguments @(
        "skills", "run", "review-only", "--dry-run", "--output", "json", "--workspace", $workspace,
        "--", "@file:note.txt"
    )
    Assert-ExitCode $skillsDryRunJson 0 "skills run dry-run json"
    Assert-Contains $skillsDryRunJson.Output '"type":"skills.runPlan"' "skills run dry-run json"
    Assert-Contains $skillsDryRunJson.Output '"dryRun":true' "skills run dry-run json"
    Assert-Contains $skillsDryRunJson.Output '"expert":"reviewer"' "skills run dry-run json"

    $pipelineList = Invoke-CaiCli -Arguments @("pipeline", "list", "--workspace", $workspace)
    Assert-ExitCode $pipelineList 0 "pipeline list"
    Assert-Contains $pipelineList.Output "C# AI CLI built-in pipelines" "pipeline list"
    Assert-Contains $pipelineList.Output "fix-review-test" "pipeline list"
    Assert-Contains $pipelineList.Output "security-review" "pipeline list"

    $pipelineListJson = Invoke-CaiCli -Arguments @("pipeline", "list", "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $pipelineListJson 0 "pipeline list json"
    Assert-Contains $pipelineListJson.Output '"type":"pipeline.list"' "pipeline list json"
    Assert-Contains $pipelineListJson.Output '"name":"review-test"' "pipeline list json"

    $pipelinePlan = Invoke-CaiCli -Arguments @(
        "pipeline", "plan", "security-review", "--workspace", $workspace,
        "--", "Review", "@file:note.txt"
    )
    Assert-ExitCode $pipelinePlan 0 "pipeline plan"
    Assert-Contains $pipelinePlan.Output "C# AI CLI pipeline plan" "pipeline plan"
    Assert-Contains $pipelinePlan.Output "security -> reviewer" "pipeline plan"
    Assert-Contains $pipelinePlan.Output "execution: plan-only; no model or tools invoked" "pipeline plan"

    $pipelinePlanJson = Invoke-CaiCli -Arguments @(
        "pipeline", "plan", "fix-review-test", "--output", "json", "--workspace", $workspace,
        "--", "Fix", "@file:note.txt"
    )
    Assert-ExitCode $pipelinePlanJson 0 "pipeline plan json"
    Assert-Contains $pipelinePlanJson.Output '"type":"pipeline.plan"' "pipeline plan json"
    Assert-Contains $pipelinePlanJson.Output '"role":"implementer"' "pipeline plan json"
    Assert-Contains $pipelinePlanJson.Output '"role":"reviewer"' "pipeline plan json"
    Assert-Contains $pipelinePlanJson.Output '"isReadOnly":true' "pipeline plan json"

    $automationList = Invoke-CaiCli -Arguments @("automation", "list", "--workspace", $workspace)
    Assert-ExitCode $automationList 0 "automation list"
    Assert-Contains $automationList.Output "C# AI CLI local automations" "automation list"
    Assert-Contains $automationList.Output "nightly-review" "automation list"

    $automationListJson = Invoke-CaiCli -Arguments @("automation", "list", "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $automationListJson 0 "automation list json"
    Assert-Contains $automationListJson.Output '"type":"automation.list"' "automation list json"
    Assert-Contains $automationListJson.Output '"name":"nightly-review"' "automation list json"

    $automationValidate = Invoke-CaiCli -Arguments @("automation", "validate", "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $automationValidate 0 "automation validate json"
    Assert-Contains $automationValidate.Output '"type":"automation.validate"' "automation validate json"
    Assert-Contains $automationValidate.Output '"diagnosticCount":0' "automation validate json"

    $automationPlan = Invoke-CaiCli -Arguments @(
        "automation", "plan", "nightly-review", "--output", "json", "--workspace", $workspace
    )
    Assert-ExitCode $automationPlan 0 "automation plan json"
    Assert-Contains $automationPlan.Output '"type":"automation.plan"' "automation plan json"
    Assert-Contains $automationPlan.Output '"enabled":false' "automation schedule preview"
    Assert-Contains $automationPlan.Output '"schedule":"0 2 * * *"' "automation schedule preview"
    Assert-Contains $automationPlan.Output 'preview only' "automation schedule preview"

    $automationDryRun = Invoke-CaiCli -Arguments @(
        "automation", "run", "nightly-review", "--dry-run", "--output", "json", "--workspace", $workspace
    )
    Assert-ExitCode $automationDryRun 0 "automation dry-run json"
    Assert-Contains $automationDryRun.Output '"type":"automation.run.dry-run"' "automation dry-run json"
    Assert-Contains $automationDryRun.Output '"execution":"none"' "automation dry-run json"

    $jobsEmpty = Invoke-CaiCli -Arguments @("jobs", "list", "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $jobsEmpty 0 "jobs list empty json"
    Assert-Contains $jobsEmpty.Output '"type":"jobs.list"' "jobs list empty json"

    $queueEmpty = Invoke-CaiCli -Arguments @("queue", "list", "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $queueEmpty 0 "queue list empty json"
    Assert-Contains $queueEmpty.Output '"type":"queue.list"' "queue list empty json"

    $queueAdd = Invoke-CaiCli -Arguments @(
        "queue", "add", "exec", "--expert", "reviewer", "--output", "json", "--workspace", $workspace,
        "--", "Review", "@file:note.txt"
    )
    Assert-ExitCode $queueAdd 0 "queue add exec json"
    Assert-Contains $queueAdd.Output '"type":"queue.add"' "queue add exec json"
    Assert-Contains $queueAdd.Output '"status":"pending"' "queue add exec json"
    $queueAddJson = $queueAdd.Output | ConvertFrom-Json
    $queuedExecId = [string]$queueAddJson.item.queueId

    $queueList = Invoke-CaiCli -Arguments @("queue", "list", "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $queueList 0 "queue list json"
    Assert-Contains $queueList.Output $queuedExecId "queue list json"

    $queueShow = Invoke-CaiCli -Arguments @("queue", "show", $queuedExecId, "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $queueShow 0 "queue show pending json"
    Assert-Contains $queueShow.Output '"type":"queue.show"' "queue show pending json"
    Assert-Contains $queueShow.Output '"status":"pending"' "queue show pending json"

    $queueRun = Invoke-CaiCli -Arguments @("queue", "run", $queuedExecId, "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $queueRun 1 "queue run missing model"
    Assert-Contains $queueRun.Output '"type":"queue.run.started"' "queue run started"
    Assert-Contains $queueRun.Output '"type":"queue.run.completed"' "queue run completed"
    Assert-Contains $queueRun.Output '"errorCode":"missing-model"' "queue run missing model"

    $queueShowCompleted = Invoke-CaiCli -Arguments @("queue", "show", $queuedExecId, "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $queueShowCompleted 0 "queue show completed json"
    $queueCompletedJson = $queueShowCompleted.Output | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$queueCompletedJson.item.latestJobId)) {
        throw "queue show completed expected latestJobId. Output:`n$($queueShowCompleted.Output)"
    }
    Assert-Contains $queueShowCompleted.Output '"status":"failed"' "queue show completed json"
    Assert-Contains $queueShowCompleted.Output '"errorCode":"missing-model"' "queue show completed json"

    $queueAddSkill = Invoke-CaiCli -Arguments @(
        "queue", "add", "skill", "review-only", "--output", "json", "--workspace", $workspace,
        "--", "Review", "@file:note.txt"
    )
    Assert-ExitCode $queueAddSkill 0 "queue add skill json"
    $queueAddSkillJson = $queueAddSkill.Output | ConvertFrom-Json
    $queuedSkillId = [string]$queueAddSkillJson.item.queueId

    $queueCancel = Invoke-CaiCli -Arguments @("queue", "cancel", $queuedSkillId, "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $queueCancel 0 "queue cancel pending"
    Assert-Contains $queueCancel.Output '"status":"canceled"' "queue cancel pending"

    $queueCancelAgain = Invoke-CaiCli -Arguments @("queue", "cancel", $queuedSkillId, "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $queueCancelAgain 1 "queue cancel non-pending"
    Assert-Contains $queueCancelAgain.Output '"errorCode":"queue-invalid-state"' "queue cancel non-pending"

    $queueCleanup = Invoke-CaiCli -Arguments @(
        "queue", "cleanup", "--status", "canceled", "--older-than-days", "30",
        "--output", "json", "--workspace", $workspace
    )
    Assert-ExitCode $queueCleanup 0 "queue cleanup recent canceled"
    Assert-Contains $queueCleanup.Output '"type":"queue.cleanup"' "queue cleanup recent canceled"
    Assert-Contains $queueCleanup.Output '"deletedCount":0' "queue cleanup recent canceled"

    $pipelineRun = Invoke-CaiCli -Arguments @(
        "pipeline", "run", "security-review", "--output", "json", "--workspace", $workspace,
        "--", "Review", "@file:note.txt"
    )
    Assert-ExitCode $pipelineRun 1 "pipeline run missing model"
    Assert-Contains $pipelineRun.Output '"type":"pipeline.result"' "pipeline run missing model"
    Assert-Contains $pipelineRun.Output '"status":"failed"' "pipeline run missing model"
    Assert-Contains $pipelineRun.Output '"role":"security"' "pipeline run missing model"
    Assert-Contains $pipelineRun.Output '"isReadOnly":true' "pipeline run read-only boundary"
    Assert-Contains $pipelineRun.Output '"allowWrites":false' "pipeline run read-only boundary"
    Assert-Contains $pipelineRun.Output '"allowShell":false' "pipeline run read-only boundary"
    Assert-Contains $pipelineRun.Output '"allowMcp":false' "pipeline run read-only boundary"
    Assert-Contains $pipelineRun.Output '"errorCode":"missing-model"' "pipeline run missing model"
    Assert-Contains $pipelineRun.Output '"kind":"task-report"' "pipeline run artifact"
    Assert-Contains $pipelineRun.Output '"remainingRisks":[' "pipeline run risks"
    $pipelineRunJson = $pipelineRun.Output | ConvertFrom-Json
    $pipelineRole = @($pipelineRunJson.report.roles)[0]
    if ([string]::IsNullOrWhiteSpace([string]$pipelineRole.queueId) -or
        [string]::IsNullOrWhiteSpace([string]$pipelineRole.jobId)) {
        throw "pipeline run expected queue and job pointers. Output:`n$($pipelineRun.Output)"
    }

    if (@($pipelineRunJson.report.roles).Count -ne 1) {
        throw "pipeline run missing-model expected reviewer short-circuit. Output:`n$($pipelineRun.Output)"
    }

    $pipelineQueueShow = Invoke-CaiCli -Arguments @(
        "queue", "show", ([string]$pipelineRole.queueId), "--output", "json", "--workspace", $workspace
    )
    Assert-ExitCode $pipelineQueueShow 0 "pipeline queue pointer"
    Assert-Contains $pipelineQueueShow.Output ([string]$pipelineRole.jobId) "pipeline queue pointer"

    $pipelineMarkdown = Invoke-CaiCli -Arguments @(
        "pipeline", "run", "security-review", "--report", "markdown", "--workspace", $workspace,
        "--", "Review", "@file:note.txt"
    )
    Assert-ExitCode $pipelineMarkdown 1 "pipeline markdown missing model"
    Assert-Contains $pipelineMarkdown.Output "C# AI CLI pipeline result" "pipeline markdown"
    Assert-Contains $pipelineMarkdown.Output "# Pipeline report: security-review" "pipeline markdown"
    Assert-Contains $pipelineMarkdown.Output "## Roles" "pipeline markdown"
    Assert-Contains $pipelineMarkdown.Output "## Artifacts" "pipeline markdown"
    Assert-Contains $pipelineMarkdown.Output "## Remaining risks" "pipeline markdown"
    Assert-Contains $pipelineMarkdown.Output "missing-model" "pipeline markdown"

    $automationManual = Invoke-CaiCli -Arguments @(
        "automation", "run", "nightly-review", "--manual", "--output", "json", "--workspace", $workspace
    )
    Assert-ExitCode $automationManual 1 "automation manual missing model"
    Assert-Contains $automationManual.Output '"type":"automation.result"' "automation manual missing model"
    Assert-Contains $automationManual.Output '"status":"failed"' "automation manual missing model"
    Assert-Contains $automationManual.Output '"targetType":"skill"' "automation manual target"
    $automationManualJson = $automationManual.Output | ConvertFrom-Json
    $automationQueueId = [string]@($automationManualJson.result.queueIds)[0]
    $automationJobId = [string]@($automationManualJson.result.jobIds)[0]
    if ([string]::IsNullOrWhiteSpace($automationQueueId) -or [string]::IsNullOrWhiteSpace($automationJobId)) {
        throw "automation manual expected queue and job pointers. Output:`n$($automationManual.Output)"
    }

    $automationQueueShow = Invoke-CaiCli -Arguments @(
        "queue", "show", $automationQueueId, "--output", "json", "--workspace", $workspace
    )
    Assert-ExitCode $automationQueueShow 0 "automation queue metadata"
    Assert-Contains $automationQueueShow.Output '"automation":"nightly-review"' "automation queue metadata"
    Assert-Contains $automationQueueShow.Output '"targetType":"skill"' "automation queue metadata"

    $automationJobShow = Invoke-CaiCli -Arguments @(
        "jobs", "show", $automationJobId, "--output", "json", "--workspace", $workspace
    )
    Assert-ExitCode $automationJobShow 0 "automation job metadata"
    Assert-Contains $automationJobShow.Output '"automation":"nightly-review"' "automation job metadata"
    Assert-Contains $automationJobShow.Output '"kind":"automation"' "automation job artifact"
    Assert-Contains $automationJobShow.Output '"path":"inline:automation/' "automation job artifact"

    $execRecordedJob = Invoke-CaiCli -Arguments @(
        "exec", "--record-job", "--job-name", "smoke-missing-model",
        "--workspace", $workspace, "--cwd", "src\app",
        "--max-turns", "1", "--max-tool-calls", "1", "--timeout-seconds", "5",
        "summarize workspace with @file:note.txt"
    )
    Assert-ExitCode $execRecordedJob 1 "exec record-job missing model"
    Assert-Contains $execRecordedJob.Output "errorCode=missing-model" "exec record-job missing model"

    $skillsRecordedDryRun = Invoke-CaiCli -Arguments @(
        "skills", "run", "review-only", "--record-job", "--dry-run", "--workspace", $workspace,
        "--", "Review", "@file:note.txt"
    )
    Assert-ExitCode $skillsRecordedDryRun 0 "skills run record-job dry-run"
    Assert-Contains $skillsRecordedDryRun.Output "dryRun: true" "skills run record-job dry-run"

    $jobsList = Invoke-CaiCli -Arguments @("jobs", "list", "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $jobsList 0 "jobs list json"
    Assert-Contains $jobsList.Output '"type":"jobs.list"' "jobs list json"
    Assert-Contains $jobsList.Output '"family":"exec"' "jobs list json"
    Assert-Contains $jobsList.Output '"family":"skills run"' "jobs list json"
    $jobsListJson = $jobsList.Output | ConvertFrom-Json
    $recordedExecJobs = @($jobsListJson.records | Where-Object { $_.command.family -eq "exec" })
    if ($recordedExecJobs.Count -lt 1) {
        throw "jobs list json expected at least one recorded exec job. Output:`n$($jobsList.Output)"
    }

    $recordedJobId = [string]$recordedExecJobs[0].jobId
    $jobsShow = Invoke-CaiCli -Arguments @("jobs", "show", $recordedJobId, "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $jobsShow 0 "jobs show json"
    Assert-Contains $jobsShow.Output '"type":"jobs.show"' "jobs show json"
    Assert-Contains $jobsShow.Output $recordedJobId "jobs show json"
    Assert-Contains $jobsShow.Output '"errorCode":"missing-model"' "jobs show json"

    $jobsExport = Invoke-CaiCli -Arguments @("jobs", "export", $recordedJobId, "--format", "markdown", "--workspace", $workspace)
    Assert-ExitCode $jobsExport 0 "jobs export markdown"
    Assert-Contains $jobsExport.Output "# C# AI CLI Job" "jobs export markdown"
    Assert-Contains $jobsExport.Output $recordedJobId "jobs export markdown"

    $ciSummary = Invoke-CaiCli -Arguments @(
        "ci", "summarize", "--job", $recordedJobId, "--output", "json", "--workspace", $workspace
    )
    Assert-ExitCode $ciSummary 0 "ci summarize json"
    Assert-Contains $ciSummary.Output '"schemaVersion":1' "ci summarize schema"
    Assert-Contains $ciSummary.Output '"type":"caicli.ci.summary"' "ci summarize type"
    Assert-Contains $ciSummary.Output '"outcome":"failure"' "ci summarize failed job outcome"
    Assert-Contains $ciSummary.Output '"recommendedExitCode":1' "ci summarize recommended exit code"
    Assert-Contains $ciSummary.Output '"rawReferencesStored":false' "ci summarize reference boundary"
    Assert-Contains $ciSummary.Output '"rawToolArgumentsStored":false' "ci summarize tool argument boundary"
    Assert-Contains $ciSummary.Output '"fullDiffStored":false' "ci summarize diff boundary"
    Assert-NotContains $ciSummary.Output '@file:note.txt' "ci summarize raw reference exclusion"

    $ciMarkdown = Invoke-CaiCli -Arguments @(
        "ci", "summarize", "--job", $recordedJobId, "--output", "markdown", "--workspace", $workspace
    )
    Assert-ExitCode $ciMarkdown 0 "ci summarize markdown"
    Assert-Contains $ciMarkdown.Output "# C-AICLI CI summary" "ci summarize markdown"
    Assert-Contains $ciMarkdown.Output "Outcome: **failure**" "ci summarize markdown outcome"

    $ciMarkdownRelativePath = ".caicli\reports\ci-summary.md"
    $ciMarkdownPath = Join-Path $workspace $ciMarkdownRelativePath
    $ciSummaryFile = Invoke-CaiCli -Arguments @(
        "ci", "summarize", "--job", $recordedJobId, "--markdown-path", $ciMarkdownRelativePath,
        "--workspace", $workspace
    )
    Assert-ExitCode $ciSummaryFile 0 "ci summarize markdown file"
    if (-not (Test-Path -LiteralPath $ciMarkdownPath -PathType Leaf)) {
        throw "ci summarize markdown file expected workspace-local output at $ciMarkdownPath"
    }
    Assert-Contains ([System.IO.File]::ReadAllText($ciMarkdownPath)) "# C-AICLI CI summary" "ci markdown file content"

    $ciCheck = Invoke-CaiCli -Arguments @(
        "ci", "check", "--job", $recordedJobId, "--workspace", $workspace
    )
    Assert-ExitCode $ciCheck 1 "ci check failed job"
    Assert-Contains $ciCheck.Output '"outcome":"failure"' "ci check failed job outcome"

    $ciMissing = Invoke-CaiCli -Arguments @(
        "ci", "check", "--job", "job_20260713T000000000Z_00000000", "--workspace", $workspace
    )
    Assert-ExitCode $ciMissing 2 "ci check config error"
    Assert-Contains $ciMissing.Output '"type":"caicli.ci.error"' "ci check config error"
    Assert-Contains $ciMissing.Output '"outcome":"config-error"' "ci check config error outcome"

    $emptyHooks = Join-Path $TempRoot "empty-hooks"
    New-Item -ItemType Directory -Path $emptyHooks -Force | Out-Null
    Invoke-Git -Name "git init" -Arguments @("-C", $workspace, "-c", "commit.gpgSign=false", "-c", "core.hooksPath=$emptyHooks", "init") | Out-Null
    Invoke-Git -Name "git config user email" -Arguments @("-C", $workspace, "config", "user.email", "smoke@example.test") | Out-Null
    Invoke-Git -Name "git config user name" -Arguments @("-C", $workspace, "config", "user.name", "CSharp AI CLI Smoke") | Out-Null
    Invoke-Git -Name "git config core.autocrlf" -Arguments @("-C", $workspace, "config", "core.autocrlf", "false") | Out-Null
    Invoke-Git -Name "git add" -Arguments @(
        "-C", $workspace,
        "-c", "commit.gpgSign=false",
        "-c", "core.hooksPath=$emptyHooks",
        "add",
        "note.txt",
        "AGENTS.md",
        "src/app/AGENTS.md",
        "src/BuggyApp/Calculator.txt",
        "tests/Verify-BuggyApp.ps1"
    ) | Out-Null
    Invoke-Git -Name "git commit" -Arguments @("-C", $workspace, "-c", "commit.gpgSign=false", "-c", "core.hooksPath=$emptyHooks", "commit", "--no-gpg-sign", "--no-verify", "-m", "Initial smoke commit") | Out-Null
    Add-Content -LiteralPath (Join-Path $workspace "note.txt") -Value "changed" -Encoding UTF8

    $diff = Invoke-CaiCli -Arguments @("diff", "--workspace", $workspace)
    Assert-ExitCode $diff 0 "diff"
    Assert-Contains $diff.Output "diff --git" "diff"

    $diffStat = Invoke-CaiCli -Arguments @("diff", "--stat", "--workspace", $workspace)
    Assert-ExitCode $diffStat 0 "diff stat"
    Assert-Contains $diffStat.Output "1 file changed" "diff stat"

    $changes = Invoke-CaiCli -Arguments @("changes", "--workspace", $workspace)
    Assert-ExitCode $changes 0 "changes"
    Assert-Contains $changes.Output "C# AI CLI changes" "changes"
    Assert-Contains $changes.Output "status: dirty" "changes"
    Assert-Contains $changes.Output "note.txt" "changes"

    $changesJson = Invoke-CaiCli -Arguments @("changes", "--output", "json", "--workspace", $workspace)
    Assert-ExitCode $changesJson 0 "changes json"
    Assert-Contains $changesJson.Output '"type":"changes.view"' "changes json"
    Assert-Contains $changesJson.Output '"status":"dirty"' "changes json"

    $missingModel = Invoke-CaiCli -Arguments @("chat", "--workspace", $workspace, "hello")
    Assert-ExitCode $missingModel 1 "chat missing model"
    Assert-Contains $missingModel.Output "localErrorCode: missing-model" "chat missing model"

    $env:OPENAI_MODEL = "gpt-smoke"
    $missingKey = Invoke-CaiCli -Arguments @("chat", "--workspace", $workspace, "hello")
    Assert-ExitCode $missingKey 1 "chat missing key"
    Assert-Contains $missingKey.Output "localErrorCode: missing-openai-api-key" "chat missing key"
    Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue

    $execMissingModel = Invoke-CaiCli -Arguments @(
        "exec", "--workspace", $workspace, "--cwd", "src\app",
        "--max-turns", "1", "--max-tool-calls", "1", "--timeout-seconds", "5",
        "summarize workspace with @file:note.txt"
    )
    Assert-ExitCode $execMissingModel 1 "exec missing model"
    Assert-Contains $execMissingModel.Output "errorCode=missing-model" "exec missing model"
    Assert-Contains $execMissingModel.Output "context.references" "exec missing model reference context"
    Assert-Contains $execMissingModel.Output "references=count=1" "exec missing model reference summary"

    $execMarkdownReport = Invoke-CaiCli -Arguments @(
        "exec", "--report", "markdown", "--workspace", $workspace,
        "--max-turns", "1", "--max-tool-calls", "1", "--timeout-seconds", "5",
        "summarize workspace with @file:note.txt"
    )
    Assert-ExitCode $execMarkdownReport 1 "exec markdown report"
    Assert-Contains $execMarkdownReport.Output "event: report.generated" "exec markdown report"
    Assert-Contains $execMarkdownReport.Output "reportMode=markdown" "exec markdown report"
    Assert-Contains $execMarkdownReport.Output "reportStatus=stdout" "exec markdown report"
    Assert-Contains $execMarkdownReport.Output "# C# AI CLI Task Report" "exec markdown report"
    Assert-Contains $execMarkdownReport.Output "## References" "exec markdown report"

    $smokeReportPath = Join-Path ".caicli\reports" "smoke.md"
    $execMarkdownReportPath = Invoke-CaiCli -Arguments @(
        "exec", "--report", "markdown", "--report-path", $smokeReportPath, "--workspace", $workspace,
        "--max-turns", "1", "--max-tool-calls", "1", "--timeout-seconds", "5",
        "summarize workspace"
    )
    Assert-ExitCode $execMarkdownReportPath 1 "exec markdown report path"
    Assert-Contains $execMarkdownReportPath.Output "reportStatus=written" "exec markdown report path"
    Assert-Contains $execMarkdownReportPath.Output "reportPath=" "exec markdown report path"
    $resolvedSmokeReportPath = Join-Path $workspace $smokeReportPath
    if (-not (Test-Path -LiteralPath $resolvedSmokeReportPath)) {
        throw "exec markdown report path did not create $resolvedSmokeReportPath."
    }
    Assert-Contains ([System.IO.File]::ReadAllText($resolvedSmokeReportPath)) "# C# AI CLI Task Report" "exec markdown report file"

    $execReviewer = Invoke-CaiCli -Arguments @(
        "exec", "--expert", "reviewer", "--workspace", $workspace,
        "--max-turns", "1", "--max-tool-calls", "1", "--timeout-seconds", "5",
        "review @file:note.txt"
    )
    Assert-ExitCode $execReviewer 1 "exec expert reviewer"
    Assert-Contains $execReviewer.Output "expert=reviewer" "exec expert reviewer"
    Assert-Contains $execReviewer.Output "errorCode=missing-model" "exec expert reviewer"

    $env:OPENAI_MODEL = "gpt-smoke"
    $execMissingKey = Invoke-CaiCli -Arguments @(
        "exec", "--output", "json", "--workspace", $workspace,
        "--max-turns", "1", "--max-tool-calls", "1", "--timeout-seconds", "5",
        "summarize workspace"
    )
    Assert-ExitCode $execMissingKey 1 "exec missing key"
    Assert-Contains $execMissingKey.Output "missing-openai-api-key" "exec missing key"

    $execSecurityJson = Invoke-CaiCli -Arguments @(
        "exec", "--expert", "security", "--output", "json", "--workspace", $workspace,
        "--max-turns", "1", "--max-tool-calls", "1", "--timeout-seconds", "5",
        "audit @file:note.txt"
    )
    Assert-ExitCode $execSecurityJson 1 "exec expert security json"
    Assert-Contains $execSecurityJson.Output '"expert":{"name":"security"' "exec expert security json"
    Assert-Contains $execSecurityJson.Output "missing-openai-api-key" "exec expert security json"
    Assert-NotContains $execSecurityJson.Output "# C# AI CLI Task Report" "exec expert security json"
    Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue

    if (-not $realModelSmokeOptIn) {
        Write-Host "real model smoke skipped: set CAICLI_REAL_MODEL_SMOKE=1 to enable read-only exec with caller OPENAI_API_KEY/OPENAI_MODEL."
    } else {
        $missingRealModelSettings = @()
        if ([string]::IsNullOrWhiteSpace($oldOpenAiKey)) {
            $missingRealModelSettings += "OPENAI_API_KEY"
        }

        if ([string]::IsNullOrWhiteSpace($oldOpenAiModel)) {
            $missingRealModelSettings += "OPENAI_MODEL"
        }

        if ($missingRealModelSettings.Count -gt 0) {
            Write-Host "real model smoke skipped: CAICLI_REAL_MODEL_SMOKE=1 requires caller $($missingRealModelSettings -join ' and ')."
        } else {
            try {
                $env:OPENAI_API_KEY = $oldOpenAiKey
                $env:OPENAI_MODEL = $oldOpenAiModel

                $noteBeforeRealModelExec = Get-Content -LiteralPath (Join-Path $workspace "note.txt") -Raw
                $realModelExec = Invoke-CaiCli -Arguments @(
                    "exec", "--workspace", $workspace, "--approval", "never",
                    "--max-turns", "2", "--max-tool-calls", "1", "--timeout-seconds", "60",
                    "Use workspace.read_text to read note.txt, then summarize it in one sentence. Do not modify files or run shell commands."
                )
                Assert-ExitCode $realModelExec 0 "real model read-only exec"
                Assert-Contains $realModelExec.Output "result: success" "real model read-only exec"
                Assert-Contains $realModelExec.Output "workspace.read_text" "real model read-only exec tool call"
                $noteAfterRealModelExec = Get-Content -LiteralPath (Join-Path $workspace "note.txt") -Raw
                if ($noteAfterRealModelExec -ne $noteBeforeRealModelExec) {
                    throw "real model read-only exec modified note.txt."
                }
            }
            finally {
                Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
                Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue
            }
        }
    }

    $patchArguments = New-ArgumentsFile "patch-arguments.json" '{"path":"note.txt","find":"before","replace":"after"}'
    $approvalDenied = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace,
        "workspace.apply_patch",
        "--arguments-file", $patchArguments
    )
    Assert-ExitCode $approvalDenied 1 "approval denied"
    Assert-Contains $approvalDenied.Output "errorCode: approval-denied" "approval denied"
    Assert-Contains $approvalDenied.Output "approvalStatus: approval-required" "approval denied"

    $readOutsideArguments = New-ArgumentsFile "read-outside-arguments.json" '{"path":"../outside.txt"}'
    $pathDenied = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace,
        "workspace.read_text",
        "--arguments-file", $readOutsideArguments
    )
    Assert-ExitCode $pathDenied 1 "path boundary"
    Assert-Contains $pathDenied.Output "errorCode: workspace-boundary-denied" "path boundary"

    $toolsJson = Invoke-CaiCli -Arguments @("tools", "list", "--workspace", $workspace, "--json")
    Assert-ExitCode $toolsJson 0 "tools list json"
    Assert-Contains $toolsJson.Output '"type":"tools.list"' "tools list json"
    Assert-Contains $toolsJson.Output '"workspace.read_text"' "tools list json"
    Assert-Contains $toolsJson.Output '"riskLevel":"read"' "tools list json"

    $stdinRead = Invoke-CaiCliWithStdin -Arguments @(
        "tools", "call", "--workspace", $workspace, "workspace.read_text", "--stdin"
    ) -StandardInput "stdin smoke"
    Assert-ExitCode $stdinRead 1 "tools call --stdin invalid JSON"
    Assert-Contains $stdinRead.Output "errorCode: invalid-tool-arguments" "tools call --stdin invalid JSON"

    $readNote = Invoke-CaiCliWithStdin -Arguments @(
        "tools", "call", "--workspace", $workspace, "workspace.read_text", "--stdin"
    ) -StandardInput '{"path":"note.txt"}'
    Assert-ExitCode $readNote 0 "tools call --stdin read"
    Assert-Contains $readNote.Output "status: succeeded" "tools call --stdin read"
    Assert-Contains $readNote.Output "before" "tools call --stdin read"

    $bugfixReadArguments = New-ArgumentsFile "bugfix-read-arguments.json" '{"path":"src/BuggyApp/Calculator.txt"}'
    $bugfixRead = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace,
        "workspace.read_text",
        "--arguments-file", $bugfixReadArguments
    )
    Assert-ExitCode $bugfixRead 0 "bugfix fixture read"
    Assert-Contains $bugfixRead.Output "expected: 41" "bugfix fixture read"

    $bugfixSearchArguments = New-ArgumentsFile "bugfix-search-arguments.json" '{"query":"expected: 41","path":"src","maxResults":5}'
    $bugfixSearch = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace,
        "workspace.search_text",
        "--arguments-file", $bugfixSearchArguments
    )
    Assert-ExitCode $bugfixSearch 0 "bugfix fixture search"
    Assert-Contains $bugfixSearch.Output "status: succeeded" "bugfix fixture search"
    Assert-Contains $bugfixSearch.Output "expected: 41" "bugfix fixture search"

    $bugfixPatchArguments = New-ArgumentsFile "bugfix-patch-arguments.json" '{"path":"src/BuggyApp/Calculator.txt","find":"expected: 41","replace":"expected: 42"}'
    $bugfixPatch = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace, "--approval", "always",
        "workspace.apply_patch",
        "--arguments-file", $bugfixPatchArguments
    )
    Assert-ExitCode $bugfixPatch 0 "bugfix fixture patch"
    Assert-Contains $bugfixPatch.Output "status: succeeded" "bugfix fixture patch"
    Assert-Contains $bugfixPatch.Output "approvalStatus: approved" "bugfix fixture patch"

    $bugfixVerifyCommand = "$(Get-SmokePowerShellExecutable) -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests\Verify-BuggyApp.ps1"
    $bugfixVerifyArguments = New-ArgumentsFile "bugfix-verify-arguments.json" (@{
        command = $bugfixVerifyCommand
        timeoutMilliseconds = 10000
    } | ConvertTo-Json -Compress)
    $bugfixVerify = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace, "--approval", "always",
        "workspace.run_shell",
        "--arguments-file", $bugfixVerifyArguments
    )
    Assert-ExitCode $bugfixVerify 0 "bugfix fixture verify"
    Assert-Contains $bugfixVerify.Output "status: succeeded" "bugfix fixture verify"
    Assert-Contains $bugfixVerify.Output "approvalStatus: approved" "bugfix fixture verify"
    Assert-Contains $bugfixVerify.Output "bugfix verification passed" "bugfix fixture verify"

    $bugfixDiffStat = Invoke-CaiCli -Arguments @("diff", "--stat", "--workspace", $workspace)
    Assert-ExitCode $bugfixDiffStat 0 "bugfix fixture diff stat"
    Assert-Contains $bugfixDiffStat.Output "Calculator.txt" "bugfix fixture diff stat"

    Set-Content -LiteralPath (Join-Path $workspaceConfigDir "config.json") -Encoding UTF8 -Value @'
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
    Assert-Contains $disabledTool.Output "errorCode: tool-disabled" "disabled tool"
    Remove-Item -LiteralPath (Join-Path $workspaceConfigDir "config.json") -Force

    $isWindowsRuntime = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
    $timeoutCommand = if ($isWindowsRuntime) { "ping -n 3 127.0.0.1 > nul" } else { "sleep 2" }
    $timeoutArguments = New-ArgumentsFile "timeout-arguments.json" (@{
        command = $timeoutCommand
        timeoutMilliseconds = 200
    } | ConvertTo-Json -Compress)
    $timeout = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace, "--approval", "always",
        "workspace.run_shell",
        "--arguments-file", $timeoutArguments
    )
    Assert-ExitCode $timeout 1 "shell timeout"
    Assert-Contains $timeout.Output "errorCode: shell-timeout" "shell timeout"

    $dangerousArguments = New-ArgumentsFile "dangerous-shell-arguments.json" '{"command":"rm -rf ."}'
    $dangerousShell = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace, "--approval", "always",
        "workspace.run_shell",
        "--arguments-file", $dangerousArguments
    )
    Assert-ExitCode $dangerousShell 1 "dangerous shell"
    Assert-Contains $dangerousShell.Output "errorCode: approval-denied" "dangerous shell"
    Assert-Contains $dangerousShell.Output "approvalStatus: dangerous-shell-denied" "dangerous shell"

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
  "messages": [
    {
      "role": "user",
      "content": "hello smoke",
      "createdAtUtc": "2024-01-01T00:00:00+00:00"
    }
  ],
  "toolCalls": [],
  "errors": []
}
'@
    $sessionList = Invoke-CaiCli -Arguments @("session", "list", "--workspace", $workspace)
    Assert-ExitCode $sessionList 0 "session list"
    Assert-Contains $sessionList.Output "- smoke" "session list"

    $sessionShow = Invoke-CaiCli -Arguments @("session", "show", "--workspace", $workspace, "smoke")
    Assert-ExitCode $sessionShow 0 "session show"
    Assert-Contains $sessionShow.Output "name: smoke" "session show"

    $changesMissingTaskReport = Invoke-CaiCli -Arguments @("changes", "--session", "smoke", "--workspace", $workspace)
    Assert-ExitCode $changesMissingTaskReport 0 "changes missing task report"
    Assert-Contains $changesMissingTaskReport.Output "taskReportSource: session:smoke" "changes missing task report"
    Assert-Contains $changesMissingTaskReport.Output "Session transcript does not contain an agent task report." "changes missing task report"

    $export = Invoke-CaiCli -Arguments @("session", "export", "--workspace", $workspace, "smoke")
    Assert-ExitCode $export 0 "session export"
    Assert-Contains $export.Output '"sessionName": "smoke"' "session export"

    $exportMarkdown = Invoke-CaiCli -Arguments @("session", "export", "--format", "markdown", "--workspace", $workspace, "smoke")
    Assert-ExitCode $exportMarkdown 0 "session export markdown"
    Assert-Contains $exportMarkdown.Output "# Session: smoke" "session export markdown"
    Assert-Contains $exportMarkdown.Output "hello smoke" "session export markdown"

    $rename = Invoke-CaiCli -Arguments @("session", "rename", "--workspace", $workspace, "smoke", "smoke-archive")
    Assert-ExitCode $rename 0 "session rename"
    Assert-Contains $rename.Output "status: renamed" "session rename"

    $delete = Invoke-CaiCli -Arguments @("session", "delete", "--workspace", $workspace, "smoke-archive")
    Assert-ExitCode $delete 0 "session delete"
    Assert-Contains $delete.Output "status: deleted" "session delete"

    $mcpScript = Join-Path $TempRoot "fake-mcp-smoke.ps1"
    $mcpStartedPath = Join-Path $TempRoot "fake-mcp-started.txt"
    New-FakeMcpServerScript -Path $mcpScript
    $mcpUserConfigDirectory = Join-Path $userProfile ".caicli"
    New-Item -ItemType Directory -Path $mcpUserConfigDirectory -Force | Out-Null
    @{
        mcpServers = @{
            smoke = @{
                enabled = $true
                transport = "stdio"
                command = Get-SmokePowerShellExecutable
                args = @(
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    $mcpScript,
                    "-StartedPath",
                    $mcpStartedPath
                )
                timeoutMilliseconds = 10000
            }
        }
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $mcpUserConfigDirectory "config.json") -Encoding UTF8

    $mcpList = Invoke-CaiCli -Arguments @("mcp", "list", "--workspace", $workspace)
    Assert-ExitCode $mcpList 0 "mcp list"
    Assert-Contains $mcpList.Output "server: smoke" "mcp list"
    Assert-Contains $mcpList.Output "status: configured" "mcp list"
    Assert-Contains $mcpList.Output "transport: stdio command" "mcp list"

    $mcpDoctor = Invoke-CaiCli -Arguments @("mcp", "doctor", "--workspace", $workspace)
    Assert-ExitCode $mcpDoctor 0 "mcp doctor"
    Assert-Contains $mcpDoctor.Output "connectionStatus: active" "mcp doctor"
    Assert-Contains $mcpDoctor.Output "MCP stdio initialize completed." "mcp doctor"

    $mcpTools = Invoke-CaiCli -Arguments @("tools", "list", "--workspace", $workspace, "--json")
    Assert-ExitCode $mcpTools 0 "mcp tools list"
    Assert-Contains $mcpTools.Output '"mcp.smoke.echo"' "mcp tools list"

    $mcpArguments = New-ArgumentsFile "mcp-arguments.json" '{"text":"hello mcp"}'
    $mcpCall = Invoke-CaiCli -Arguments @(
        "tools", "call", "--workspace", $workspace, "--approval", "always",
        "mcp.smoke.echo",
        "--arguments-file", $mcpArguments
    )
    Assert-ExitCode $mcpCall 0 "mcp tools call"
    Assert-Contains $mcpCall.Output "status: succeeded" "mcp tools call"
    Assert-Contains $mcpCall.Output "approvalStatus: approved" "mcp tools call"
    Assert-Contains $mcpCall.Output "echo: hello mcp" "mcp tools call"

    $logsClear = Invoke-CaiCli -Arguments @("logs", "clear", "--workspace", $workspace)
    Assert-ExitCode $logsClear 0 "logs clear"
    Assert-Contains $logsClear.Output "Cleared" "logs clear"

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
