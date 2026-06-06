# 第 18 周 MCP Config 与 `mcp list` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加 MCP 配置模型和 `mcp list` 命令。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 05 计划：`docs_md/plans/05_mcp_project_workflows.plan.md`
- 第 17 周回顾：`docs_md/weekly/17_week_review.md`

## 本周范围

- MCP server 配置 schema。
- 加载 user/workspace MCP 配置。
- `caicli mcp list` 显示 server 名称、启用状态和 transport 摘要。
- disabled server 保持 inactive。

## 任务清单

- [ ] Step 1: 添加 MCP 配置模型。
- [ ] Step 2: 添加 MCP 配置 loader。
- [ ] Step 3: 添加 `mcp list` 命令。
- [ ] Step 4: 添加禁用 server 测试。
- [ ] Step 5: 运行 `dotnet build` 和 `dotnet test`。
- [ ] Step 6: 创建 `18_week_review.md` 并更新总排期。

## 验收标准

- MCP 配置可加载。
- 已禁用 servers 保持 inactive。

