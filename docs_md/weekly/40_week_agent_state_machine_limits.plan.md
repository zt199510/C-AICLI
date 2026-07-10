# 第 40 周 Agent State Machine 与执行预算 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 抽出可测试的 agent state machine，统一 step/tool/time/output budget、stop reason、loop error 和事件契约。

## 来源

- `docs_md/weekly/28_week_exec_json_events.plan.md`
- `docs_md/weekly/29_week_agent_run_loop_v1.plan.md`
- `docs_md/weekly/39_week_real_model_tool_continuation.plan.md`

## 本周范围

- 明确定义 agent run 状态、step、stop reason。
- 统一 loop budget：maxSteps、maxToolCalls、timeout、output truncation。
- 统一模型失败、工具失败、审批拒绝和用户取消的状态。
- 保持 text/NDJSON/session/trace 事件字段一致。

## 任务清单

- [ ] Step 1: 定义 `AgentRunState`、`AgentStep`、`AgentStopReason` 和 `AgentLoopError`。
- [ ] Step 2: 将现有 loop limit 逻辑迁移到 state machine。
- [ ] Step 3: 增加配置与 CLI options：max steps、max tool calls、timeout。
- [ ] Step 4: 统一 stop reason 到 text/JSON event、session transcript 和 trace。
- [ ] Step 5: 覆盖 approval denied、tool disabled、tool timeout、model error、loop budget exceeded。
- [ ] Step 6: 确保 cancel/timeout 后不会继续写文件或启动新工具。
- [ ] Step 7: 增加 deterministic fake model 测试。
- [ ] Step 8: 更新 runtime logging spec 和 capability status。
- [ ] Step 9: 运行 build/test 并创建 `40_week_review.md`。

## 验收标准

- 所有 agent run 都有明确 final status 和 stop reason。
- budget exceeded 是可预期退出，不是异常崩溃。
- text、NDJSON、trace 对同一次 run 的 step 顺序一致。
- 审批拒绝和工具失败不会被误标为成功。

## 周回顾模板

```markdown
## 第 40 周回顾

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

第 41 周输入：
-
```

