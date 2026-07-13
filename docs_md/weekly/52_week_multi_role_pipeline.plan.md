# 第 52 周 Multi-role Pipeline Implementation Plan

状态：已验收

**Goal:** 基于 0.3.3 expert profiles 和 0.4.0 job/queue 底座，实现本地多角色 pipeline v1，使 implementer/reviewer/tester 等角色可以按固定顺序协作，并保留每个角色的工具边界、报告和 artifact。

## 来源

- `docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md`
- `docs_md/weekly/51_week_task_queue_run_control.plan.md`
- `docs_md/release/capability_status.md`

## 本周范围

- 定义 pipeline manifest/plan/result DTO。
- 增加内置 pipelines：`fix-review-test`、`review-test`、`security-review`。
- 每个 role 复用现有 `--expert`、`skills`、report 和 job artifact。
- reviewer/security role 必须保持只读；tester role 可运行验证但仍遵守 shell policy/approval。
- pipeline final report 合并各 role 的 task report metadata，不保存 raw referenced content。

本周明确不做：

- 不做自动 model role routing。
- 不做多个模型/provider 自动分配。
- 不做并行多 agent worker。
- 不做远程团队协作。

## 用户入口草案

```powershell
caicli pipeline list
caicli pipeline plan fix-review-test --workspace . -- "Fix failing tests"
caicli pipeline run fix-review-test --workspace . -- "Fix failing tests"
caicli pipeline run security-review --report markdown --workspace . -- "@folder:src"
```

## 任务清单

- [x] Step 1: 定义 pipeline DTO、role step、role boundary 和 final report schema。
- [x] Step 2: 新增 built-in pipeline catalog。
- [x] Step 3: 实现 `pipeline list/plan` text/json，不调用模型、不运行工具。
- [x] Step 4: 实现 `pipeline run` 顺序执行，复用 queue/job/exec/skills path。
- [x] Step 5: 确保 reviewer/security role 不注册 patch/shell/MCP write-capable tools。
- [x] Step 6: 合并 role reports、artifacts、warnings 和 remaining risks。
- [x] Step 7: 增加 fake/offline pipeline tests。
- [x] Step 8: 更新 smoke/docs。
- [x] Step 9: 运行 build/test/smoke 并创建 `52_week_review.md`。

## 验收标准

- pipeline plan 可审计，run 结果可通过 job history 追踪。
- 每个 role 的 expert、tool boundary、commands、verification、risks 可复核。
- pipeline failure 不吞掉中间 role 的 artifact。
- 无真实模型凭据时有稳定 failure/dry-run path。
