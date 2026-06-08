# 第 37 周 可观测性、日志与 Trace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 增强 CLI 调试能力，让模型调用、工具调用、审批、错误和耗时都能被定位。

## 来源

- 运行时日志规格：`docs_md/spec/runtime_logging_diagnostics.md`
- 第 29 周计划：`docs_md/weekly/29_week_agent_run_loop_v1.plan.md`
- 第 34 周计划：`docs_md/weekly/34_week_tool_system_hardening.plan.md`

## 本周范围

- 全局 `--verbose`。
- 全局 `--trace` 或 `CAICLI_TRACE=1`。
- 新增 `logs show`、`logs clear`、`logs path`。
- 事件耗时和 correlation id。
- JSON event stream 和本地日志格式保持一致的核心字段。

## 任务清单

- [ ] Step 1: 定义 diagnostic context：command id、session id、workspace、timestamp。
- [ ] Step 2: 给 model call、tool call、approval、MCP call 增加 duration 和 status。
- [ ] Step 3: 实现全局 `--verbose`，显示更详细的人类可读诊断。
- [ ] Step 4: 实现全局 `--trace`，写入 trace-level 本地日志。
- [ ] Step 5: 新增 `logs path`。
- [ ] Step 6: 新增 `logs show --tail <n>`。
- [ ] Step 7: 新增 `logs clear`，默认只清理 CLI 自己的日志目录。
- [ ] Step 8: 增加测试覆盖日志路径、tail、clear、secret redaction、trace disabled 默认行为。
- [ ] Step 9: 更新 runtime logging spec 和 release docs。
- [ ] Step 10: 运行 build/test 并创建 `37_week_review.md`。

## 验收标准

- 默认日志不打印 API key。
- `--trace` 能帮助定位一次 exec 中的模型和工具调用顺序。
- `logs show --tail 20` 可读取最近日志。
- `logs clear` 不会删除 workspace 文件。

## 周回顾模板

```markdown
## 第 37 周回顾

状态：计划中 | 进行中 | 已稳固 | 已验收

已完成：
-

验证：
- 命令：
- 结果：

运行时说明：
-

风险：
-

第 38 周输入：
-
```
