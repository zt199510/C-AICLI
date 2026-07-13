# C# AI CLI 0.4.0 Engineering Automation Platform 排期

更新时间：2026-07-13

## 起点确认

0.3.3 已完成并验收为当前 release line。`docs_md/release/final_acceptance_0.3.3.md` 已记录 release artifact、deterministic zip、packaged smoke、capability status 和 Deferred 边界。

本次进入 0.4.0 规划前已复核：

- `dotnet build src\CSharpAiCli.sln -c Release` 通过，0 warnings，0 errors。
- `dotnet test src\CSharpAiCli.sln -c Release --no-build` 通过，1060 passed，0 failed，0 skipped。
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1` 通过；默认 real model smoke 按设计跳过。
- `tools\Build-Release.ps1` 连续两次生成 `artifacts/release/caicli-0.3.3-win-x64.zip`，size `32371265` bytes，SHA256 `3EA45A7366BD3E7EDC940E1737D786EA0D4DFB6C8ECB963D6FED668F6E903BF1`。

当前可用基础：

- `exec` 已具备真实 OpenAI SDK tool-call continuation、fake/offline contract、bounded loop、patch/verification/failure retry、`review.gate`、`taskReport`、markdown report、expert profiles 和 session/trace。
- `skills list/run` 已提供本地 workflow pack 入口，内置 .NET packs 可作为后续 automation pipeline 的 task template。
- `changes`、`diff`、`review`、`session export`、trace/logs、workflow profile、MCP stdio v1 和 release smoke 已可作为自动化复核与交付物基础。
- 仍未启用 task queue、job history、artifact index、多角色 pipeline、automation schedule、CI/PR integration、daemon/API/SSE。

## 产品目标

0.4.0 的目标是把 0.3.3 的单次受控 agent 任务升级为可排队、可复核、可复用、可集成的本地工程自动化底座：

```text
job record -> task plan -> controlled execution -> artifact/report -> review/check -> history/export
```

这条线借鉴 WorkBuddy 的 Automation、多任务和可交付结果思路，但保持 C-AICLI 的定位：Windows-first、C#/.NET-first、私有模型友好、CLI-first、本地可审计。

## 非目标

- 不做桌面 App、Electron/Web UI、TUI 或 IM 控制。
- 不做远程 SaaS 平台、远程 job runner、远程 marketplace 或云端任务分发。
- 不在 0.4.0 默认开启常驻 daemon、HTTP API、SSE 或远程控制；如实现必须是显式 preview，并且默认关闭。
- 不做团队知识库、复杂权限平台、组织级审计后台或多人在线协作。
- 不做自动 model role routing；多角色先复用本地 expert/skill/report 边界。
- 不做 Gerber/TIFF 真实工具链执行。
- 不让 automation、queue、daemon、CI 或 API 绕过 approval、workspace guard、dirty workspace checks、secret redaction、shell policy、disabled tools、trace/log 或 release smoke。

## 0.4.0 成功标准

0.4.0 验收时，至少应满足以下结果：

- 每次受控任务可以形成稳定 job record，包含 job id、workspace、command source、status、timestamps、task report、trace/session/report/artifact pointers 和 redacted metadata。
- 用户可以通过 CLI 查看 job history、job detail、artifact list 和导出结果；这些命令默认只读、不调用模型、不运行 shell、不写 workspace。
- 简单 task queue 能表达 pending/running/succeeded/failed/canceled 状态，并可用 fake/offline path 测试。
- 多角色 pipeline 能复用已有 expert profiles，例如 implementer/reviewer/tester，并保持每个角色的 tool boundary、report 和 remaining risks 可复核。
- Automation/CI 入口优先输出稳定 JSON/markdown artifacts，不要求 daemon 或远程服务。
- 默认 smoke 仍 credential-free；真实模型 smoke 仍 opt-in。
- release docs 准确标注 Accepted、Preview 和 Deferred 能力。

## 阶段划分

| 阶段 | 周次 | 主题 | 目标 |
|---|---:|---|---|
| Phase 16 | 50-51 | Job history、artifact store 与 task queue foundation | 建立本地 job record/artifact index，并为后续 queue/orchestration 提供稳定 DTO、storage 和 CLI read surface。 |
| Phase 17 | 52-53 | Multi-role pipelines 与 local automation | 用已有 expert/skills/report 组合出 implementer/reviewer/tester pipeline，并支持本地 automation dry-run/manual trigger。 |
| Phase 18 | 54-55 | CI artifacts 与 local API preview | 输出 CI/PR 友好的 report/check artifacts；评估本地 daemon/API/SSE preview，但默认关闭。 |
| Phase 19 | 56-57 | Hardening 与 0.4.0 release acceptance | 对 automation/security/smoke/docs 做收口，再回归 build/test/smoke/package，记录 final acceptance、zip size/SHA256 和 Deferred 边界。 |

## 状态说明

- `计划中` 表示已有目标和验收范围，尚未创建 week review。
- `已稳固` 表示功能契约、离线测试和主要错误路径稳定，但仍需在 release acceptance 中回归。
- `已验收` 表示已有对应 `NN_week_review.md`，并记录 build/test/docs/smoke 证据。

## 逐周计划

| 周 | 计划文档 | 状态 | 主要目标 | 周末验收 |
|---:|---|---|---|---|
| 50 | `50_week_job_history_artifact_store.plan.md` | 已验收 | 定义 job record、artifact index、job store 与 `jobs list/show/export` 只读入口；先不做 queue 执行。 | `exec`/`skills` 可选择性记录 redacted job metadata；jobs text/json 可读；不写 workspace、不泄露 raw references/secrets。 |
| 51 | `51_week_task_queue_run_control.plan.md` | 已验收 | 增加本地 task queue v1、job state transitions、cancel/retry/cleanup 语义和 fake/offline queue runner。 | pending/running/succeeded/failed/canceled 状态可测试；queue 不绕过 approval/security。 |
| 52 | `52_week_multi_role_pipeline.plan.md` | 计划中 | 复用 expert profiles 组成 implementer/reviewer/tester pipeline，明确每个 role 的 tool boundary 和 report 合并策略。 | fake pipeline 覆盖实现->复核->验证；reviewer/security 保持只读；最终 artifact 可复核。 |
| 53 | `53_week_local_automation_commands.plan.md` | 计划中 | 增加 local automation 配置、dry-run、manual trigger 和 schedule preview；不做后台常驻。 | automation list/validate/run --dry-run 稳定；不会默认定时执行或远程执行。 |
| 54 | `54_week_ci_pr_artifacts.plan.md` | 计划中 | 输出 CI/PR 友好的 JSON/markdown/check summary artifacts，并提供 deterministic exit-code policy。 | CI smoke 可在无模型凭据下验证 schema；report/check 不写出 raw secrets。 |
| 55 | `55_week_local_api_daemon_preview.plan.md` | 计划中 | 评估本地 daemon/HTTP API/SSE preview，默认关闭，仅绑定 localhost，复用 job store 和 approval boundaries。 | preview 不影响默认 CLI；API 不能扩大工具权限；docs 明确 Deferred/Preview。 |
| 56 | `56_week_automation_security_smoke_docs_hardening.plan.md` | 计划中 | 对 job/queue/pipeline/automation/API preview 做安全、观测、smoke 和文档收口。 | high-risk boundary、redaction、storage cleanup、smoke/docs 均回归；不新增大功能。 |
| 57 | `57_week_cli_0_4_release_acceptance.plan.md` | 计划中 | 0.4.0 发布验收、版本元数据、CHANGELOG、capability status、release zip。 | build/test/smoke 通过；0.4.0 zip size/SHA256 记录；Deferred 边界准确。 |

## 关键设计原则

- Job record 是 0.4.0 的事实底座；queue、automation、CI 和 API 都应引用同一套 job/task report/artifact metadata。
- 不创建第二套 report truth；job artifacts 必须复用 `AgentTaskReport`、markdown report、trace、session 和 changes view 事实源。
- 默认不把 raw referenced content、raw tool arguments、raw secrets 或完整 diff 永久化到 job history；只保存 bounded redacted metadata 和 artifact pointers。
- 写 workspace 文件、运行 shell、启动 MCP、patch 和 high-risk tools 仍必须走现有 approval/security path。
- 所有新增命令都必须支持稳定 text 输出；面向自动化/CI/API 的命令优先提供 JSON 输出。
- 单元测试不依赖真实网络；真实模型和真实 daemon/API smoke 必须显式 opt-in。
- Local API/daemon 是后置 preview，不是 0.4.0 的第一目标；不能阻塞 job/task/report CLI 主线。

## 验证基线

每周至少运行：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
```

涉及 smoke、job store、automation 或 release 的周额外运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

发布候选额外运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
```

## 建议实现顺序

1. Week 50 先建立 job record/artifact index 和只读 jobs CLI，避免 queue/API 先行导致事实源分裂。
2. Week 51 再实现 task queue/run control，复用 Week 50 job store。
3. Week 52 用已有 expert/skill 组合出 multi-role pipeline，不新增 provider/model routing。
4. Week 53-54 把 automation 和 CI/PR artifacts 建在 queue/job/report 之上。
5. Week 55 才评估 local API/daemon preview，默认关闭，不能扩大权限。
6. Week 56 做 security/smoke/docs hardening，只收口不扩范围。
7. Week 57 完成 0.4.0 release acceptance。

## 周回顾模板

每周结束时创建对应 `NN_week_review.md`：

```markdown
## 第 N 周回顾

状态：计划中 | 进行中 | 已稳固 | 已验收

已完成：
-

验证：
- 命令：
- 结果：

运行时说明：
-

风险：
-

第 N+1 周输入：
-
```
