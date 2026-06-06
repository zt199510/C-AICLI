# 第 11 周 Patch Applier、Dirty Workspace 与文件编辑审批 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加 patch applier、dirty workspace 检测和文件编辑审批流程。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 03 计划：`docs_md/plans/03_tools_safety_file_editing.plan.md`
- 第 10 周回顾：`docs_md/weekly/10_week_review.md`

## 本周范围

- 解析或表示最小 patch 结构。
- patch 预览展示路径、摘要和 diff。
- 写入前要求审批策略。
- 应用前重新读取目标文件并验证上下文。
- dirty workspace 状态进入预览和 transcript。

## 不做

- 不做复杂三方合并。
- 不修改未在 patch 中声明的文件。
- 不绕过审批写文件。

## 任务清单

- [ ] Step 1: 定义 `IPatchApplier`、patch preview 和 apply result。
- [ ] Step 2: 实现单文件 patch 应用和上下文校验。
- [ ] Step 3: 添加 dirty workspace 检测。
- [ ] Step 4: 添加 approval policy fake 和生产默认拒绝/询问策略。
- [ ] Step 5: 将 patch 工具接入工具注册表和 transcript。
- [ ] Step 6: 运行 `dotnet build` 和 `dotnet test`。
- [ ] Step 7: 创建 `11_week_review.md` 并更新总排期。

## 验收标准

- patch 可预览、审批、应用。
- 目标文件预览后变化会拒绝应用。

