# 第 20 周 MCP Tool Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 MCP 工具桥接到通用工具注册表。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 05 计划：`docs_md/plans/05_mcp_project_workflows.plan.md`
- 第 19 周回顾：`docs_md/weekly/19_week_review.md`

## 本周范围

- MCP tool metadata 映射到 `IToolRegistry`。
- 已禁用 MCP 工具不可执行。
- MCP 工具调用结果进入 transcript。

## 任务清单

- [ ] Step 1: 添加 MCP tool adapter。
- [ ] Step 2: 接入通用工具注册表。
- [ ] Step 3: 添加禁用工具拒绝测试。
- [ ] Step 4: 添加 fake MCP tool 调用测试。
- [ ] Step 5: 运行 `dotnet build` 和 `dotnet test`。
- [ ] Step 6: 创建 `20_week_review.md` 并更新总排期。

## 验收标准

- MCP 工具在启用后可列出并调用。

