# 第 19 周 MCP Connection Manager 与 `mcp doctor` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加 MCP 连接管理器和 `mcp doctor`。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 05 计划：`docs_md/plans/05_mcp_project_workflows.plan.md`
- 第 18 周回顾：`docs_md/weekly/18_week_review.md`

## 本周范围

- MCP 连接状态抽象。
- `mcp doctor` 检查配置和连接可用性。
- 连接失败以安全诊断显示。

## 任务清单

- [ ] Step 1: 添加 MCP connection manager contract。
- [ ] Step 2: 实现基础连接诊断。
- [ ] Step 3: 添加 `mcp doctor` 命令。
- [ ] Step 4: 添加未配置、禁用、连接失败测试。
- [ ] Step 5: 运行 `dotnet build` 和 `dotnet test`。
- [ ] Step 6: 创建 `19_week_review.md` 并更新总排期。

## 验收标准

- 已配置 servers 的连接诊断可用。

