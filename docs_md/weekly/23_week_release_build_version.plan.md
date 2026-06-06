# 第 23 周 Release Build Script 与版本元数据 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加发布构建脚本和版本元数据。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 06 计划：`docs_md/plans/06_packaging_release_hardening.plan.md`
- 第 22 周回顾：`docs_md/weekly/22_week_review.md`

## 本周范围

- Release build 命令。
- Windows self-contained publish。
- 版本号和产物目录。
- 构建脚本不包含密钥。

## 任务清单

- [ ] Step 1: 添加版本元数据。
- [ ] Step 2: 添加 `tools/Build-Release.ps1`。
- [ ] Step 3: 添加 Release build 测试或脚本 smoke。
- [ ] Step 4: 运行 `dotnet build -c Release` 和 `dotnet test -c Release`。
- [ ] Step 5: 创建 `23_week_review.md` 并更新总排期。

## 验收标准

- 发布构建命令会产生产物。

