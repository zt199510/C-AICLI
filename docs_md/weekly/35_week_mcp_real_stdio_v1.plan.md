# 第 35 周 MCP Stdio 真连接 v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 MCP 从配置/诊断占位推进到 stdio server 的真实握手、工具发现和工具调用 MVP。

## 来源

- 第 34 周计划：`docs_md/weekly/34_week_tool_system_hardening.plan.md`
- 当前 MCP config/list/doctor/bridge 实现。
- `docs_md/release/known_limitations.md`

## 本周范围

- 支持 stdio MCP server process 启动。
- 实现 MCP initialize/list tools/call tool 的最小 JSON-RPC 流程。
- `mcp doctor` 执行真实握手。
- MCP tools 注册进统一 tool registry。
- remote transport 继续 Deferred。

## 任务清单

- [ ] Step 1: 定义 MCP JSON-RPC request/response DTO。
- [ ] Step 2: 实现 stdio process transport，带 timeout、stderr capture 和 workspace cwd guard。
- [ ] Step 3: 实现 MCP initialize handshake。
- [ ] Step 4: 实现 `tools/list`。
- [ ] Step 5: 实现 `tools/call`。
- [ ] Step 6: 将 MCP discovered tools 映射为 `ToolDefinition`。
- [ ] Step 7: 修改 `mcp doctor`，对 stdio server 做真实握手诊断。
- [ ] Step 8: 增加 fake MCP server 测试 fixture，覆盖 handshake、list、call、timeout、invalid JSON。
- [ ] Step 9: 更新 known limitations：stdio MCP v1 可用，remote MCP 仍 Deferred。
- [ ] Step 10: 运行 build/test 并创建 `35_week_review.md`。

## 验收标准

- fake stdio MCP server 能被 `mcp doctor` 标记为 active。
- MCP tool 能通过 `tools list` 看到并通过 `tools call` 调用。
- server timeout 和 invalid response 不会挂死 CLI。
- disabled MCP server 不会启动。

## 周回顾模板

```markdown
## 第 35 周回顾

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

第 36 周输入：
-
```
