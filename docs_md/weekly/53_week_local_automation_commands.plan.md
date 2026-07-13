# 第 53 周 Local Automation Commands Implementation Plan

状态：已验收

**Goal:** 在 queue/job/pipeline 稳定后，引入本地 automation 配置、验证、dry-run 和手动触发入口，为定时检查、定时 review、定时报告预留 CLI-first 自动化能力，但不启动后台常驻服务。

## 来源

- `docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md`
- `docs_md/weekly/51_week_task_queue_run_control.plan.md`
- `docs_md/weekly/52_week_multi_role_pipeline.plan.md`

## 本周范围

- 定义 automation manifest/schema，先支持 workspace-local JSON。
- 增加 `automation list/validate/plan/run --dry-run/run --manual`。
- automation target 可以是 queue item、skill run 或 pipeline run。
- schedule 字段只做 preview/validation，不默认定时执行。
- 所有 run 都写入 job/queue history。

本周明确不做：

- 不做真正后台 scheduler。
- 不做 Windows Task Scheduler 注册。
- 不做远程触发、webhook 或 team automation。
- 不做 daemon/API。

## 用户入口草案

```powershell
caicli automation list --workspace .
caicli automation validate --workspace .
caicli automation plan nightly-review --workspace .
caicli automation run nightly-review --dry-run --workspace .
caicli automation run nightly-review --manual --workspace .
```

## 任务清单

- [x] Step 1: 定义 automation manifest、trigger、target、safety DTO。
- [x] Step 2: 实现 workspace-local manifest loading 与 validation diagnostics。
- [x] Step 3: 增加 `automation list/validate/plan` text/json。
- [x] Step 4: 增加 `automation run --dry-run`，不调用模型、不运行工具。
- [x] Step 5: 增加 `automation run --manual`，复用 queue/pipeline/exec 安全路径。
- [x] Step 6: 将 automation metadata 写入 job/queue artifacts。
- [x] Step 7: 增加 tests 覆盖 invalid manifest、unsafe target、disabled tools、redaction。
- [x] Step 8: 更新 smoke/docs。
- [x] Step 9: 运行 build/test/smoke 并创建 `53_week_review.md`。

## 验收标准

- automation manifest 只读加载，不执行脚本。
- dry-run 不调用模型、不运行 shell、不写 workspace。
- manual run 不绕过 approval/security。
- docs 明确 schedule 是 preview，不是后台定时执行。
