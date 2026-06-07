# 第 10 周 Workspace Guard、File Read 与 Search Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加工作区路径保护、文件读取工具和搜索工具。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 03 计划：`docs_md/plans/03_tools_safety_file_editing.plan.md`
- 第 9 周回顾：`docs_md/weekly/09_week_review.md`

## 本周范围

- 实现 `IWorkspaceGuard`，统一规范化 Windows 路径。
- 阻止 `..`、工作区外路径、大小写绕过和 junction/symlink 越界。
- 文件读取工具支持文本文件、大小上限和二进制拒绝。
- 搜索工具支持工作区内文本搜索，跳过二进制和过大文件。
- 工具失败以结构化结果返回 agent loop。

## 不做

- 不写文件。
- 不运行 shell。
- 不做跨工作区读取。

## 任务清单

- [x] Step 1: 添加 `WorkspaceGuard` 和路径安全测试。
- [x] Step 2: 添加 junction/symlink 越界测试，Windows 上可运行。
- [x] Step 3: 添加 file read tool。
- [x] Step 4: 添加 search tool。
- [x] Step 5: 接入工具注册表和离线 agent loop。
- [x] Step 6: 运行 `dotnet build` 和 `dotnet test`。
- [x] Step 7: 创建 `10_week_review.md` 并更新总排期。

## 验收标准

- 工作区外读取、`..`、大小写路径和 junction/symlink 越界在测试中被阻止。
- 工作区内文本读取和搜索可用。
