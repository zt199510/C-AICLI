# 第 12 周 Shell Runner、命令审批与危险命令阻断 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加 shell runner、命令审批、危险命令阻断、超时和输出截断。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 03 计划：`docs_md/plans/03_tools_safety_file_editing.plan.md`
- 第 11 周回顾：`docs_md/weekly/11_week_review.md`

## 本周范围

- `IShellRunner` 只允许 workspace 内 cwd。
- 默认超时和 stdout/stderr 截断。
- 危险命令模式拒绝。
- shell 命令默认需要审批。
- 结果写入 transcript，包含拒绝、超时、exit code 和截断标记。

## 不做

- 不支持后台驻留服务。
- 不自动运行破坏性命令。
- 不跨 shell 拼接删除/移动命令。

## 任务清单

- [x] Step 1: 定义 shell command request/result。
- [x] Step 2: 实现危险命令检测。
- [x] Step 3: 实现受限 shell runner。
- [x] Step 4: 接入 approval policy。
- [x] Step 5: 添加超时、cwd 越界、stdout/stderr 截断测试。
- [x] Step 6: 运行 `dotnet build` 和 `dotnet test`。
- [x] Step 7: 创建 `12_week_review.md` 并更新总排期。

## 验收标准

- 无害命令审批后运行。
- 危险模式、cwd 越界和超时路径有测试。
