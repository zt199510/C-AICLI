# 第 29 周 Agent Run Loop v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `exec` 从 deterministic task 扩展为可进行模型规划和工具调用的本地 agent loop v1。

## 来源

- 第 28 周计划：`docs_md/weekly/28_week_exec_json_events.plan.md`
- 工具能力状态：`docs_md/release/capability_status.md`
- 已知限制：`docs_md/release/known_limitations.md`

## 本周范围

- 实现 direct OpenAI backend 的 agent loop v1。
- 支持 read/search/git status/git diff/shell/patch 工具调用。
- 每轮记录 model event、tool call event、tool result event 和 final response event。
- 增加最大轮数、最大工具调用数、单次模型调用超时和整体超时。
- 保留 deterministic fallback 测试路径，确保无 API key 环境仍可跑测试。

## 任务清单

- [ ] Step 1: 设计 `IAgentRunner` direct 实现与 `AgentRunRequest`、`AgentRunResult`、`AgentRunEvent`。
- [ ] Step 2: 将工具 registry schema 转换为模型可用的 tool definition。
- [ ] Step 3: 实现模型响应解析：final text、tool call、tool arguments。
- [ ] Step 4: 实现工具执行回写模型上下文。
- [ ] Step 5: 增加 loop limits：`--max-turns`、`--max-tool-calls`、`--timeout-seconds`。
- [ ] Step 6: 将 `caicli exec` 接到 agent runner。
- [ ] Step 7: transcript 记录 agent loop 的工具调用和结果摘要。
- [ ] Step 8: 增加 fake model loop 测试，覆盖单工具、多工具、工具失败、达到轮数上限。
- [ ] Step 9: 更新 docs，说明 `exec` 已进入 agentic v1，`run` 仍是兼容入口。
- [ ] Step 10: 运行 build/test 并创建 `29_week_review.md`。

## 验收标准

- fake model 可以驱动 `exec` 调用至少一个 workspace 工具并生成 final response。
- 工具失败不会导致未处理异常。
- 超出轮数或工具调用数时返回清晰错误。
- 无真实 API key 的测试路径保持稳定。

## 周回顾模板

```markdown
## 第 29 周回顾

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

第 30 周输入：
-
```
