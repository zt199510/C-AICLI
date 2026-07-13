# Week 50-57 Execution Prompts

更新时间：2026-07-13

用途：本文件集中保存 0.4.0 Week 50-57 的执行计划提示词。每一段都可以直接复制给执行 agent，用于按周推进实现、验证和周回顾。

通用要求：

- 工作目录：`D:\AI\C-AICLI`。
- 先阅读本周 plan、0.4.0 总排期、roadmap、0.3.3 final acceptance 和相关 release docs。
- 按 plan 的 checkbox 逐项执行，完成一步就更新对应 checkbox，不要到最后一次性标记。
- 优先复用现有架构、DTO、renderer、CLI command factory、trace/session/report/redaction/security path。
- 不绕过 approval、workspace guard、dirty workspace checks、secret redaction、shell policy、disabled tools、MCP startup boundary 或 smoke。
- 默认 smoke 不依赖模型凭据；真实模型、daemon/API smoke 必须 opt-in。
- 收尾必须运行本周要求的 build/test/smoke，并创建对应 `NN_week_review.md`。

## Week 50 Prompt

```text
你现在执行 C-AICLI 第 50 周计划：Job History 与 Artifact Store Foundation。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/spec/product_positioning_and_roadmap.md
- docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md
- docs_md/weekly/50_week_job_history_artifact_store.plan.md
- docs_md/release/final_acceptance_0.3.3.md
- docs_md/release/capability_status.md
- docs_md/spec/runtime_logging_diagnostics.md

目标：
为 0.4.0 建立本地 job history 与 artifact index 底座。实现 job record/job artifact/job store DTO、用户级本地 job store、只读 jobs list/show/export 入口，并把 exec 与 skills run 通过显式 --record-job 接入 job recording。

范围边界：
- job recording 先采用 --record-job opt-in，不改变默认 exec/skills run 持久化行为。
- jobs list/show/export 只读，不调用模型、不运行 shell、不执行 patch、不启动 MCP、不写 workspace。
- job record 只能保存 redacted metadata 和 artifact pointers，不保存 raw referenced content、raw secrets、raw tool arguments 或完整 diff。
- 不实现 queue、pipeline、automation、CI provider、daemon、HTTP API 或远程控制。

执行步骤：
1. 按 plan 确认 --record-job、--job-name 和 job store 默认位置。
2. 新增 JobRecord、JobStatus、JobArtifact、JobCommandSummary 等 DTO 与 JSON schema。
3. 实现用户级 JobRecordStore，支持 create/update/read/list 和 corrupt record diagnostics。
4. 增加 jobs list/show/export text/json/markdown renderer 与 CLI。
5. 将 --record-job 接入 exec，覆盖 success/failure/reference failure/report artifact。
6. 将 --record-job 接入 skills run dry-run 与 non dry-run metadata。
7. 复用现有 redaction，确保 job record 不泄露 secrets/raw references/raw tool args/full diff。
8. 增加 unit/CLI/smoke tests，更新 docs 与 runtime diagnostics。
9. 运行：
   $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
   dotnet build src\CSharpAiCli.sln -c Release
   dotnet test src\CSharpAiCli.sln -c Release --no-build
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
10. 创建 docs_md/weekly/50_week_review.md，记录实际交付、验证结果、风险、Deferred 边界和 Week 51 输入。
```

## Week 51 Prompt

```text
你现在执行 C-AICLI 第 51 周计划：Task Queue 与 Run Control。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md
- docs_md/weekly/50_week_job_history_artifact_store.plan.md
- docs_md/weekly/50_week_review.md
- docs_md/weekly/51_week_task_queue_run_control.plan.md
- docs_md/release/capability_status.md
- docs_md/spec/runtime_logging_diagnostics.md

目标：
在 Week 50 job history/artifact store 底座之上，实现本地 task queue v1 和 run control。queue 能表达 pending/running/succeeded/failed/canceled 状态，并且 queue run 仍复用现有 exec/skills 安全路径与 job history。

范围边界：
- 不做 daemon、scheduler、远程 runner、并发 worker pool。
- 不做 multi-role pipeline。
- 不做 CI/PR provider integration。
- queue add 只记录待执行 request，不立即绕过审批执行。
- queue run 不提升权限，不覆盖 approval/shell policy/disabled tools。

执行步骤：
1. 定义 TaskQueueItem、status、attempt、error code 和 JSON schema。
2. 实现本地 queue store，复用 Week 50 storage/redaction 策略。
3. 增加 queue add/list/show text/json CLI。
4. 实现 queue run <id>，将 queue item 展开为受控 exec 或 skills run。
5. 实现 pending-only cancel 语义和稳定错误码。
6. 实现最小 cleanup，避免删除 running/unknown 状态。
7. 将 queue run 结果写入 job history，并建立 queue->job pointer。
8. 增加 queue store、CLI、state transition、redaction/security boundary tests。
9. 更新 smoke script、SmokeTestScriptTests 和 release docs。
10. 运行 build/test/smoke。
11. 创建 docs_md/weekly/51_week_review.md，记录交付、验证、风险和 Week 52 输入。
```

## Week 52 Prompt

```text
你现在执行 C-AICLI 第 52 周计划：Multi-role Pipeline。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md
- docs_md/weekly/51_week_task_queue_run_control.plan.md
- docs_md/weekly/51_week_review.md
- docs_md/weekly/52_week_multi_role_pipeline.plan.md
- docs_md/release/capability_status.md

目标：
基于 0.3.3 expert profiles 和 0.4.0 job/queue 底座，实现本地 multi-role pipeline v1。内置 pipeline 可组合 implementer/reviewer/tester/security 等角色，并保留每个角色的 tool boundary、task report、artifact 和 remaining risks。

范围边界：
- 不做自动 model role routing。
- 不做多模型/provider 自动分配。
- 不做并行 worker。
- 不做远程团队协作。
- reviewer/security role 必须保持只读，不能注册 patch/shell/MCP write-capable tools。

执行步骤：
1. 定义 pipeline DTO、role step、role boundary 和 final report schema。
2. 新增 built-in pipeline catalog：fix-review-test、review-test、security-review。
3. 实现 pipeline list/plan text/json，不调用模型、不运行工具。
4. 实现 pipeline run 顺序执行，复用 queue/job/exec/skills path。
5. 确保 reviewer/security role 保持 read-only boundary。
6. 合并 role reports、artifacts、warnings 和 remaining risks。
7. 增加 fake/offline pipeline tests 和 CLI tests。
8. 更新 smoke/docs。
9. 运行 build/test/smoke。
10. 创建 docs_md/weekly/52_week_review.md，记录交付、验证、风险和 Week 53 输入。
```

## Week 53 Prompt

```text
你现在执行 C-AICLI 第 53 周计划：Local Automation Commands。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md
- docs_md/weekly/52_week_multi_role_pipeline.plan.md
- docs_md/weekly/52_week_review.md
- docs_md/weekly/53_week_local_automation_commands.plan.md
- docs_md/release/security_model.md

目标：
在 queue/job/pipeline 稳定后，引入本地 automation manifest、validate、plan、dry-run 和 manual run。automation target 可以是 queue item、skill run 或 pipeline run；schedule 字段只做 preview/validation，不默认定时执行。

范围边界：
- 不做后台 scheduler。
- 不注册 Windows Task Scheduler。
- 不做远程触发、webhook 或 team automation。
- 不做 daemon/API。
- automation manifest 只读加载，不执行脚本。

执行步骤：
1. 定义 automation manifest、trigger、target、safety DTO 和 JSON schema。
2. 实现 workspace-local manifest loading 与 validation diagnostics。
3. 增加 automation list/validate/plan text/json。
4. 增加 automation run --dry-run，不调用模型、不运行工具。
5. 增加 automation run --manual，复用 queue/pipeline/exec 安全路径。
6. 将 automation metadata 写入 job/queue artifacts。
7. 增加 invalid manifest、unsafe target、disabled tools、redaction/security tests。
8. 更新 smoke/docs，明确 schedule 是 preview，不是后台执行。
9. 运行 build/test/smoke。
10. 创建 docs_md/weekly/53_week_review.md，记录交付、验证、风险和 Week 54 输入。
```

## Week 54 Prompt

```text
你现在执行 C-AICLI 第 54 周计划：CI/PR Artifacts。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md
- docs_md/weekly/53_week_local_automation_commands.plan.md
- docs_md/weekly/53_week_review.md
- docs_md/weekly/54_week_ci_pr_artifacts.plan.md
- docs_md/release/capability_status.md

目标：
为 CI/PR 场景提供稳定 JSON/markdown/check summary artifacts 和 deterministic exit-code policy，让 job、queue、pipeline、automation 结果可被外部流水线消费，但不绑定具体云端平台。

范围边界：
- 不调用 GitHub/GitLab/Azure DevOps API。
- 不创建 PR comment。
- 不做 webhook 或 remote callback。
- 不上传 artifact 到云端。
- CI artifact 不包含 raw secrets、raw references、raw tool args 或完整 diff。

执行步骤：
1. 定义 CI summary/check/annotation schema。
2. 增加 job->CI artifact renderer。
3. 实现 ci summarize/check 或等价 jobs export --format ci-json。
4. 明确 deterministic exit-code policy，并增加 tests。
5. 确保 CI artifact redaction 与 storage boundary 正确。
6. 增加 credential-free CI artifact smoke。
7. 更新 docs，给出 GitHub Actions/Azure DevOps 手动调用示例，但不内置 provider integration。
8. 运行 build/test/smoke。
9. 创建 docs_md/weekly/54_week_review.md，记录交付、验证、风险和 Week 55 输入。
```

## Week 55 Prompt

```text
你现在执行 C-AICLI 第 55 周计划：Local API / Daemon Preview。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md
- docs_md/weekly/54_week_ci_pr_artifacts.plan.md
- docs_md/weekly/54_week_review.md
- docs_md/weekly/55_week_local_api_daemon_preview.plan.md
- docs_md/release/security_model.md

目标：
评估并可选实现本地 daemon/HTTP API/SSE preview，为 IDE/Web UI/远程控制预留入口。preview 默认关闭，仅绑定 localhost，且不能扩大 CLI 权限。

范围边界：
- 不绑定 0.0.0.0。
- 不做公网访问、认证平台、多用户权限或远程 agent。
- 不做浏览器 UI。
- 不让 API 绕过 approval、workspace guard、shell policy、disabled tools、job/queue security boundary。
- 如果风险不可控，可以只保留 daemon doctor/api routes 文档化 preview，不强行实现 start。

执行步骤：
1. 写明 preview threat model 和默认关闭策略。
2. 定义 API route contract，优先 read-only jobs/queue endpoints。
3. 如实现 daemon start，限制 localhost，并拒绝 remote bind。
4. 将 API 操作全部复用 CLI service path 和 existing security boundaries。
5. 增加 opt-in smoke，不进入默认 CI 前提。
6. 更新 docs，将 daemon/API 标为 Preview 或 Deferred。
7. 运行 build/test/smoke。
8. 创建 docs_md/weekly/55_week_review.md，记录交付、验证、风险和 Week 56 输入。
```

## Week 56 Prompt

```text
你现在执行 C-AICLI 第 56 周计划：Automation Security / Smoke / Docs Hardening。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md
- docs_md/weekly/50_week_review.md
- docs_md/weekly/51_week_review.md
- docs_md/weekly/52_week_review.md
- docs_md/weekly/53_week_review.md
- docs_md/weekly/54_week_review.md
- docs_md/weekly/55_week_review.md
- docs_md/weekly/56_week_automation_security_smoke_docs_hardening.plan.md
- docs_md/release/security_model.md
- tools/Invoke-SmokeTests.ps1

目标：
对 Week 50-55 引入的 job、queue、pipeline、automation、CI artifacts 和 API preview 做安全、观测、smoke、文档收口，确保 Week 57 只做 release acceptance，不再补实现范围。

范围边界：
- 不新增新的 automation target。
- 不新增 provider integration。
- 不扩展 daemon/API preview。
- 不做 0.5.0 垂直能力。
- 只修正 security gap、schema drift、docs drift、smoke 缺口和 release readiness 问题。

执行步骤：
1. 汇总 Week 50-55 Deferred/Preview 边界。
2. 回归所有新增命令的只读/写入边界。
3. 增加 redaction/security regression tests。
4. 增加 smoke 覆盖 job/queue/pipeline/automation/CI artifact credential-free 路径。
5. 更新 release docs、runtime logging diagnostics、known limitations、capability status、troubleshooting。
6. 修正 Accepted/Preview/Deferred 标记。
7. 运行 build/test/smoke。
8. 创建 docs_md/weekly/56_week_review.md，列出 release acceptance 前剩余问题。
```

## Week 57 Prompt

```text
你现在执行 C-AICLI 第 57 周计划：CLI 0.4.0 发布验收。

工作目录：D:\AI\C-AICLI

必须先阅读：
- docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md
- docs_md/weekly/50_week_review.md
- docs_md/weekly/51_week_review.md
- docs_md/weekly/52_week_review.md
- docs_md/weekly/53_week_review.md
- docs_md/weekly/54_week_review.md
- docs_md/weekly/55_week_review.md
- docs_md/weekly/56_week_review.md
- docs_md/weekly/57_week_cli_0_4_release_acceptance.plan.md
- docs_md/release/final_acceptance_0.3.3.md
- tools/Build-Release.ps1
- tools/Invoke-SmokeTests.ps1

目标：
对 Week 50-56 的工程自动化平台化能力做最终验收，更新版本元数据到 0.4.0，生成 deterministic release package，并记录 final acceptance、zip size 和 SHA256。

范围边界：
- 本周只做 release acceptance、文档修正和阻塞级缺陷修复。
- 不新增大功能。
- 不把 Preview/Deferred 能力写成 Accepted。
- 默认 smoke credential-free；real model smoke 和 daemon/API smoke 仍必须 opt-in。

执行步骤：
1. 汇总 Week 50-56 review 状态，列出未完成项。
2. 确认所有 Deferred/Preview 能力边界仍准确。
3. 更新 version metadata 到 0.4.0。
4. 更新 CHANGELOG、configuration、quickstart、security model、known limitations、capability status、troubleshooting。
5. 创建 docs_md/release/final_acceptance_0.4.0.md。
6. 运行：
   $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
   dotnet build src\CSharpAiCli.sln -c Release
   dotnet test src\CSharpAiCli.sln -c Release --no-build
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
   artifacts\release\caicli-0.4.0-win-x64\caicli.exe version
   Get-FileHash -Algorithm SHA256 artifacts\release\caicli-0.4.0-win-x64.zip
7. 记录 release zip size/SHA256。
8. 创建 docs_md/weekly/57_week_review.md，记录 release decision。
```
