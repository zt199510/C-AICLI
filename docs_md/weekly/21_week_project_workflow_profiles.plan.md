# 第 21 周 Project Workflow Registry 与 Validation Profiles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加项目工作流注册表和验证 profiles。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 05 计划：`docs_md/plans/05_mcp_project_workflows.plan.md`
- 第 20 周回顾：`docs_md/weekly/20_week_review.md`

## 本周范围

- 项目 workflow registry。
- validation profile 配置。
- 工作流建议验证命令，但执行仍走审批 shell runner。
- 路径来自 profile 或 `--workspace`。

## 任务清单

- [x] Step 1: 添加 workflow registry contract。
- [x] Step 2: 添加 validation profile loader。
- [x] Step 3: 添加 workflow status/validate 命令骨架。
- [x] Step 4: 添加路径来源和审批测试。
- [x] Step 5: 运行 `dotnet build` 和 `dotnet test`。
- [x] Step 6: 创建 `21_week_review.md` 并更新总排期。

## 验收标准

- 工作流可以建议已配置的验证命令。
- 路径来自 profile 或 `--workspace`。
