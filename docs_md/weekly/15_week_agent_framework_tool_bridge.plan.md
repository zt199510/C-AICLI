# 第 15 周 Microsoft Agent Framework Tool Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将本地工具桥接到 Microsoft Agent Framework 后端。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 04 计划：`docs_md/plans/04_agent_framework_adapter.plan.md`
- 第 14 周回顾：`docs_md/weekly/14_week_review.md`

## 本周范围

- 把本地 `IToolRegistry` 映射到 framework 工具定义。
- framework 后端调用 read/search 工具。
- 工具失败、安全拒绝和审批结果保持结构化。

## 任务清单

- [x] Step 1: 添加 framework tool bridge。
- [x] Step 2: 映射 read/search 工具 metadata。
- [x] Step 3: 添加 framework backend fake/integration 测试。
- [x] Step 4: 运行 `dotnet build` 和 `dotnet test`。
- [x] Step 5: 创建 `15_week_review.md` 并更新总排期。

## 验收标准

- 框架后端可以调用读取和搜索工具。
