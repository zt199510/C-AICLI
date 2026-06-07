# 第 25 周 Smoke Tests 与发布加固 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加 smoke tests 和发布加固修复。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 06 计划：`docs_md/plans/06_packaging_release_hardening.plan.md`
- 第 24 周回顾：`docs_md/weekly/24_week_review.md`

## 本周范围

- `tools/Invoke-SmokeTests.ps1`。
- 覆盖缺 key、审批拒绝、路径越界、工具禁用、命令超时。
- smoke 使用干净临时 workspace/user profile。

## 任务清单

- [x] Step 1: 添加 smoke test 脚本。
- [x] Step 2: 添加干净 workspace fixture。
- [x] Step 3: 覆盖缺 key 和模型配置错误。
- [x] Step 4: 覆盖审批拒绝、路径越界和工具禁用。
- [x] Step 5: 运行 Release build 和 smoke tests。
- [x] Step 6: 创建 `25_week_review.md` 并更新总排期。

## 固化记录

- 新增 `tools/Invoke-SmokeTests.ps1`，默认使用 `artifacts/release/caicli-0.1.0-win-x64/caicli.exe`。
- Smoke 使用临时 user profile、临时 workspace 和 workspace 外 fixture 文件。
- 覆盖 `version`、`doctor`、`chat` 缺 model、`chat` 缺 key、审批拒绝、路径越界、工具禁用、shell timeout、`run` 小任务、session export 和 session clear。
- 新增 `tools list/call`、`run`、`session export/clear` CLI 表面命令用于发布验收。
- 新增 `disabledTools` 配置字段，支持 user/workspace config 合并禁用工具。
- 已验证 Release build、Release tests 和 smoke tests 均通过。

## 验收标准

- Smoke test 脚本在干净测试工作区通过。
