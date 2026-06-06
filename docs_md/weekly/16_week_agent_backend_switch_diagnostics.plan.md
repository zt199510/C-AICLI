# 第 16 周 Agent Backend Switch 与回退诊断 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加后端配置开关和 framework 回退诊断。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 04 计划：`docs_md/plans/04_agent_framework_adapter.plan.md`
- 第 15 周回顾：`docs_md/weekly/15_week_review.md`

## 本周范围

- 配置 `agent.backend` 支持 direct/framework。
- doctor 显示 backend 状态。
- framework 不可用时给出清晰原因，direct 不受影响。

## 任务清单

- [ ] Step 1: 扩展配置模型，添加 backend 字段。
- [ ] Step 2: 添加 backend resolver。
- [ ] Step 3: 更新 doctor/config get 输出。
- [ ] Step 4: 添加 framework 缺失/禁用测试。
- [ ] Step 5: 运行 `dotnet build` 和 `dotnet test`。
- [ ] Step 6: 创建 `16_week_review.md` 并更新总排期。

## 验收标准

- direct 和 framework 后端都可以通过配置选择。
- framework 缺失时 doctor 解释原因。

