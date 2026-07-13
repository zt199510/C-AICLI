# 第 53 周回顾

状态：已验收

## 已完成

- 定义 automation schema v1：`AutomationManifest`、`AutomationTrigger`、`AutomationTarget`、`AutomationSafety`、catalog/source/diagnostic、plan、run metadata/result、run id 与 strict JSON schema。Manifest 只接受 trigger/target/safety 数据，不接受未知 `script`、`command` 或 approval 字段。
- 新增 workspace-local `.caicli/automations` JSON loading。目录与每个 manifest 都经过 workspace guard；单文件限制为 256 KiB；未知字段、重复名称、schema version、五字段 numeric cron preview、time zone、cwd 边界、target existence 和 safety capability mismatch 都形成稳定 diagnostics。
- 新增 `automation list/validate/plan` text/json。三者只读取本地 manifest、skill catalog 与 built-in pipeline catalog，不调用模型、不构建 tool registry、不运行工具、不启动 MCP、不创建 queue/job、不写 command log 或 workspace。
- 新增 `automation run <name> --dry-run` text/json。Dry-run 复用同一 validated plan 和 schedule preview，非持久化，不创建 automation run id、queue/job、trace/session/report 或 workspace 文件。
- 新增 `automation run <name> --manual`。Queue/skill target 创建标准 queue item 后调用现有 `queue run`；pipeline target 调用现有 `pipeline run`，每个 role 再进入 queue/job/exec/skills path。Automation 不传 `--approve` 或 `--approval`，不创建第二套 executor/report truth。
- Automation target 支持 queue item（exec/skill family）、local skill 与 built-in pipeline。Target 只能引用已有 expert/skill/pipeline；manifest safety 必须覆盖 target 的固有能力，但 safety 声明本身不授予权限。
- Queue request 与 job command 写入 bounded/redacted automation name、run id、mode、source path 与 target type。Job artifact 增加 `inline:automation/<run-id>` pointer；pipeline 各 role 共享同一 automation run correlation。
- Schedule trigger 只做 validation/preview，输出固定 `enabled=false`。本周没有后台 scheduler、Windows Task Scheduler 注册、daemon worker、API/webhook、remote trigger 或 team automation。
- 新增 9 个 automation Core/CLI tests，覆盖 strict schema、valid schedule preview、unknown script field、unsafe capability、list/validate/plan/dry-run 零状态、manual skill queue/job artifact、pipeline correlation、disabled tools、approval denial 与 secret redaction。
- 默认 packaged smoke 新增 automation list/validate/plan/dry-run text/json，以及无凭据 manual skill failure、queue/job pointers、automation metadata 和 inline artifact 检查。
- 更新 capability status、quickstart、known limitations、security model、troubleshooting 与 CHANGELOG，准确区分当前 local automation commands 和 Deferred automatic schedule execution/daemon/API/remote boundaries。

## 验证

- 命令：`dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。
- 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，1117 passed，0 failed，0 skipped。
- 定向命令：`dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --filter "FullyQualifiedName~Automation"`
- 结果：通过，9 passed，0 failed，0 skipped。
- 受影响路径回归：`dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TaskQueue|FullyQualifiedName~Pipeline|FullyQualifiedName~JobRecord"`
- 结果：通过，43 passed，0 failed，0 skipped。
- 支撑命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1`
- 结果：通过；刷新 packaged executable 供最终 smoke 验证。首次未设置用户级 SDK PATH 时只发现系统 SDK `10.0.301`，与 `global.json` 的 `9.0.308` 不兼容；补齐既定 PATH 后通过，未修改 SDK 锁定。
- 命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过，输出 `smoke tests passed`；真实模型 smoke 按设计跳过，并提示设置 `CAICLI_REAL_MODEL_SMOKE=1`。
- Artifact：`artifacts/release/caicli-0.3.3-win-x64.zip`，size `32463421` bytes，SHA256 `623EEEEB3C4A4C5C9D9276854AEAC2B0BE3D8694186613B5D972BC546FC9D13E`。该包仅用于本周 packaged smoke 支撑，不改变当前 `0.3.3` release decision；0.4.0 deterministic package acceptance 仍在 Week 57。
- 检查：`git diff --check`
- 结果：通过，无 whitespace error。

## 运行时说明

- Workspace manifests 位于 `.caicli/automations/*.json` 或子目录 `manifest.json`。当前只支持 JSON；未知字段会使 manifest 无效，不会被忽略后进入可运行 catalog。
- Trigger 支持 `manual` 和 `schedule`。当前 schedule validator 接受 numeric five-field cron preview；它不计算 next-run、不启动 timer，plan/dry-run 始终显示 `enabled=false`。
- Target `queue` 要求 `family=exec|skill`；skill target 要求现有 skill name；pipeline target 只能引用 `fix-review-test`、`review-test` 或 `security-review`。`cwd` 必须留在 workspace 内。
- `automation validate` 在任一 manifest 有 diagnostic 时返回 exit code `1`；`list` 仍列出其余有效 manifest 并附 diagnostics；无效 manifest 不能被 plan/run 选中。
- `automation run` 必须且只能指定一个 `--dry-run` 或 `--manual`。Dry-run 不持久化；manual run 形成 queue/job truth，并返回 correlation pointers。
- 无模型凭据时 manual target 返回 delegated `missing-model` failure，但 queue/job/automation artifact 仍完整。Disabled tool、approval、shell policy 与 read-only expert/skill failure code 保持原样。

## 风险

- Schedule 是语法 preview，不是完整 cron engine。当前不支持 named month/weekday、next-run calculation、DST execution semantics 或 misfire policy；因为本周不执行 schedule，这些不影响 manual path，但后续若引入 scheduler 必须采用成熟调度库重新定义契约。
- Automation safety 是可验证声明，不是 sandbox。真正的权限边界仍由 approval、workspace guard、dirty-workspace checks、shell policy、dangerous-command detection、disabled tools、MCP startup policy 和 expert/skill boundary 提供。
- Manual automation 是同步、单进程编排；没有 automation-level retry/resume、parallel worker、lease/heartbeat 或进程中断恢复。Pipeline target 仍继承 first-failure short-circuit。
- Automation result 是命令输出，不是独立 history store。持久事实仍是 queue/job/taskReport/artifact；不应再创建第二套 automation report truth。
- Queue request 会按现有策略持久化 bounded redacted task。Secret-like task 内容变为 `[redacted]` 后可能改变 delegated model 看到的语义，这是已有 queue 安全权衡。
- 本周没有运行 opt-in 真实模型 automation smoke。真实 provider 的质量、延迟、配额以及多 role behavior 仍是外部风险。

## Deferred 边界

- 不实现后台 scheduler、Windows Task Scheduler 注册、automatic schedule execution、misfire/retry policy 或 daemon worker。
- 不实现 API/webhook、remote trigger/runner/control、team automation、CI/PR provider 或并行 worker。
- 不执行 manifest script/command，不持久化或注入 approval override，不扩大 disabled tools、shell/MCP policy 或 expert/skill boundary。

## 第 54 周输入

- CI/PR artifacts 应复用 automation result、queue/job/taskReport/artifact pointers 和现有 redaction，不创建第二套执行器或 report truth。
- CI validate/plan/check summary 必须 credential-free、deterministic，并明确区分 manifest validation failure、delegated target failure 与 policy denial exit code。
- CI 输出应包含 automation/pipeline/queue/job correlation、warnings、remaining risks 与 skipped roles，但不得包含 raw references、raw tool arguments、raw secrets 或 full diff。
- CI smoke 默认继续不依赖模型凭据；真实 model/provider 或外部 CI provider smoke 必须显式 opt-in。
- Week 54 不应借 CI 入口引入 daemon/API/webhook/remote runner，也不能绕过 approval、workspace guard、dirty workspace checks、shell policy、disabled tools、MCP startup boundary、trace/session/report 或 smoke。
