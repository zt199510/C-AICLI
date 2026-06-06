# 第 14 周 Microsoft Agent Framework Adapter Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加 Microsoft Agent Framework 适配器项目或命名空间骨架。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 04 计划：`docs_md/plans/04_agent_framework_adapter.plan.md`
- 第 13 周回顾：`docs_md/weekly/13_week_review.md`

## 本周范围

- 定义或确认产品内核 `IAgentRunner`。
- 添加 adapter 项目或命名空间。
- direct 后端不依赖 framework 类型。
- 如果 framework 包不可用，适配器可标记为实验或 stub，但 core/CLI 必须构建。

## 任务清单

- [ ] Step 1: 确认 direct runner 与 `IAgentRunner` 边界。
- [ ] Step 2: 添加 adapter 项目/命名空间。
- [ ] Step 3: 添加构建和边界测试。
- [ ] Step 4: 更新阶段 04 风险记录。
- [ ] Step 5: 运行 `dotnet build` 和 `dotnet test`。
- [ ] Step 6: 创建 `14_week_review.md` 并更新总排期。

## 验收标准

- 适配器构建成功，且不需要修改 CLI 命令代码。
- direct 后端保持可用。

