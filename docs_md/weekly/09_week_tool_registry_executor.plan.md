# 第 9 周 Tool Registry 与 Offline Agent Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加工具注册表、工具执行器和工具调用转录记录，建立阶段 03 的离线 fake model + fake tool agent loop。

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 03 计划：`docs_md/plans/03_tools_safety_file_editing.plan.md`
- 第 8 周回顾：`docs_md/weekly/08_week_review.md`

## 本周范围

- 定义 provider-neutral 工具接口、工具 metadata 和参数 schema。
- 添加工具注册表和工具执行上下文。
- 添加 fake test tool，用于离线 agent loop 测试。
- 将工具调用请求、审批占位、工具结果摘要写入 transcript `toolCalls`。
- 添加最小 `IAgentRunner` 或等价 direct runner 骨架，只支持 fake model/tool 的离线循环。
- 覆盖工具成功、工具失败、未知工具、参数错误和 transcript 记录。

## 不做

- 不实现真实文件读取、搜索、patch 或 shell。
- 不连接真实 OpenAI tool calling。
- 不执行工作区外路径访问。

## 任务清单

- [x] Step 1: 定义 `ITool`、`ToolDefinition`、`ToolExecutionContext`、`ToolExecutionResult`。
- [x] Step 2: 实现 `ToolRegistry`，支持注册、列出和按名称查找。
- [x] Step 3: 实现 `ToolExecutor`，统一捕获安全错误和工具异常。
- [x] Step 4: 扩展 transcript tool call schema，并保持 Week 7 空数组兼容。
- [x] Step 5: 添加 fake model + fake tool 离线循环测试。
- [x] Step 6: 运行 `dotnet build` 和 `dotnet test`。
- [x] Step 7: 创建 `09_week_review.md` 并更新总排期。

## 验收标准

- fake model + fake test tool 可以通过离线 agent loop 调用。
- 工具结果和失败原因可写入 transcript。
- 未知工具和参数错误不会崩溃 CLI/core。
