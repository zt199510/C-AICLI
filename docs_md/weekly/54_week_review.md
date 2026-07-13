# 第 54 周回顾

状态：已验收

## 已完成

- 定义 CI artifact schema v1：`CiArtifact`、job/correlation/summary/check/annotation/artifact pointer/redaction DTO，以及 strict `CiArtifactJsonSchema`。根对象固定为 `schemaVersion=1`、`type=caicli.ci.summary`，对象禁止未知字段，annotation 与 artifact pointer 均有数量和字符串边界。
- 增加 `JobRecord -> CiArtifact` renderer。事实源继续是现有 user-level job store、`JobTaskReportSummary`、artifact pointers 与 automation/pipeline/queue correlation；没有创建第二套执行器、report truth 或 CI history store。
- 增加 `ci summarize --job <id> --output json|markdown` 与 `ci check --job <id> --fail-on none|warnings|risks`。两个命令只读 job store，不创建 model client、tool registry、MCP connection、queue/job、trace/session 或 command log。
- `ci summarize --markdown-path <path>` 复用现有 `ReportPathResolver`：只允许 workspace 内新文件，拒绝 escape、目录和 overwrite。没有显式 path 时 JSON/markdown 只写 stdout，不持久化 CI artifact。
- 明确 deterministic exit-code policy：`success=0`、默认 `warning=0`、source failure 或命中的 `--fail-on` threshold 为 `1`、missing/corrupt input、unsupported status 或 unsafe source redaction boundary 为 `2`。`ci check` artifact 中的 `recommendedExitCode` 与实际进程退出码一致；`ci summarize` 成功投影 failed job 时返回 `0`，由 artifact 保留推荐码 `1`。
- CI JSON/markdown 不读取或输出 job task text、raw reference content、task-report command/verification details、raw tool arguments 或 full diff。允许输出的 summary/warning/risk/correlation/pointer 字符串再次经过 secret redaction。source job 未声明 `secretsRedacted=true` 且 raw references/tool arguments/full diff 均未存储时，投影变为 `config-error` 并清空 artifact pointers。
- annotation 使用 provider-neutral `notice|warning|failure`；没有输出 GitHub/Azure/GitLab annotation protocol。queue id 从现有 job label 提取，pipeline run id 从现有 bounded warning metadata 提取，automation correlation 复用 `AutomationRunMetadata`。
- 默认 packaged smoke 增加 credential-free CI JSON、markdown stdout、workspace-local markdown file、failed check `1` 和 missing-input config error `2` 覆盖；真实模型 smoke 仍需 `CAICLI_REAL_MODEL_SMOKE=1` 显式启用。
- 新增 `docs_md/release/ci_artifacts.md`，记录 schema、exit policy、redaction/storage boundary、GitHub Actions 与 Azure DevOps 手动 YAML。同步更新 capability status、quickstart、security model、known limitations 与 CHANGELOG，明确没有 provider API、PR comment、webhook/callback 或 cloud upload。

## 验证

- 命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。
- 命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，1131 passed，0 failed，0 skipped。
- 定向命令：`dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --no-build --filter "(FullyQualifiedName~CiArtifactTests)|(FullyQualifiedName~CiCliTests)"`
- 结果：通过，14 passed，0 failed，0 skipped；覆盖 schema、JSON/markdown、outcome/exit mapping、fail threshold、read-only CLI、redaction/raw-field exclusion、unsafe boundary、workspace guard 与 no-overwrite。
- smoke 支撑命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1`
- 结果：通过，刷新 packaged executable 供本周 smoke 使用。
- 命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过，输出 `smoke tests passed`；真实模型 smoke 按设计跳过并提示设置 `CAICLI_REAL_MODEL_SMOKE=1`。
- smoke 支撑 artifact：`artifacts/release/caicli-0.3.3-win-x64.zip`，size `32476676` bytes，SHA256 `3CF5AF09B52C525690BE300005144E39053967B79A5F44EC076AB523036B3F19`。该包仅用于本周 packaged smoke，不改变现有 `0.3.3` release decision；`0.4.0` deterministic package acceptance 仍在 Week 57。
- 检查：`git diff --check`
- 结果：通过，无 whitespace error；仅有仓库既有 Git line-ending conversion warning。

## 运行时说明

- `ci summarize` 是 artifact generation 命令。只要能安全投影一个已存在 job，即使 job 本身 failed，也返回 `0`；source outcome 和后续建议码位于 `check.outcome` / `check.recommendedExitCode`。
- `ci check` 是 gate 命令。failed/canceled/approval-required/non-zero source job 始终返回 `1`；warning 默认不失败，调用方可用 `--fail-on risks` 或 `--fail-on warnings` 提升为 `1`。
- planned/running job 产生 `warning`，unknown status 产生 `config-error`。missing/corrupt job 产生稳定 `caicli.ci.error` envelope 和 exit code `2`。
- CI artifact 的 `generatedAtUtc` 使用 source job 的 `updatedAtUtc`，不会因重复导出引入 wall-clock 漂移。JSON property contract、annotation ordering 和 artifact pointer ordering来自同一 job record，可重复解析。
- CI 命令不会重新执行 job，也不会读取 trace/session/report 文件内容。artifact 列表只是现有 local pointer/hash projection；本地路径仍可能暴露 repository layout，应按敏感 pipeline metadata 处理。
- GitHub Actions/Azure DevOps 示例中的 env/YAML 是调用方手工接线。C-AICLI 不识别 provider context，也不创建 checks、comments、timeline records 或 uploads。

## 风险

- v1 一次只投影一个 job。pipeline final report 仍是命令输出；多 job 聚合、persistent pipeline history、cross-job check rollup 与 provider status synchronization 未实现。
- redaction 是 bounded metadata contract 与 best-effort secret pattern redaction，不是数据泄漏的形式化证明。source redaction flags 不安全时会拒绝 pointers，但调用方仍应限制 CI stdout/file 的访问和保留期。
- warning 默认 exit `0` 是兼容非阻断 summary 的策略；需要把 remaining risks 或 warnings 作为 gate 的流水线必须显式传 `--fail-on risks|warnings`。
- `--markdown-path` 不覆盖旧文件，也没有 retention/rotation/cleanup。长期流水线需由调用方选择唯一文件名或清理 workspace。
- 本周没有运行 opt-in real model smoke，也没有外部 GitHub/Azure/GitLab provider smoke。provider quality、network、permissions 和 YAML runner behavior 不属于本地 credential-free acceptance。

## Deferred 边界

- 不实现 GitHub/GitLab/Azure DevOps API、checks/status API、PR comment、provider annotation protocol、artifact upload、webhook、remote callback 或 remote runner。
- 不新增 daemon/API/SSE、background scheduler、concurrent worker 或 remote control；不让 CI projection 执行 job、shell、patch、MCP 或扩大 approval/security policy。
- 不把 raw references、raw tool arguments、raw secrets、full diff 或 artifact file contents 写入 CI artifact。

## 第 55 周输入

- Local API/daemon preview 必须默认关闭并只绑定 localhost；真实 daemon/API smoke 必须显式 opt-in，不能进入默认 credential-free smoke。
- API preview 应复用同一 `JobRecordStore`、queue/job DTO、CI renderer、exit policy、trace/session/report pointers 与 redaction contract，不创建第二套 job/report truth。
- API 只能暴露 bounded redacted metadata 和 pointers；不得读取/返回 raw reference、tool args、full diff、trace/session/report artifact contents 或 secret values。
- API/daemon 不能注入 approval override、扩大 disabled tools、跳过 workspace/dirty/shell/MCP startup boundary，或把 provider integration、webhook、remote callback 带入 Week 55。
- Week 55 应明确 process lifecycle、localhost binding、request bounds、concurrency/termination behavior 和 preview/Deferred 标记，并保持普通 CLI 行为不受影响。
