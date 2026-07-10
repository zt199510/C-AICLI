# 第 39 周 真实模型工具调用 Continuation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 direct backend 从 fake/offline tool loop 推进到真实模型 tool-call continuation，使模型可以请求本地工具、接收工具结果并继续生成。

## 来源

- `docs_md/weekly/29_week_agent_run_loop_v1.plan.md`
- `docs_md/weekly/34_week_tool_system_hardening.plan.md`
- `docs_md/weekly/37_week_observability_logs_trace.plan.md`
- `docs_md/weekly/46_week_cli_0_3_real_agent_schedule.md`

## 本周范围

- direct backend 真实 tool-call continuation。
- 将统一 tool schema 暴露给真实模型调用路径。
- 将 `ToolExecutionResult` structured payload 和 errorCode 回写给模型。
- 保持 fake model 与真实模型路径共享同一事件契约。
- 真实模型 smoke 默认 opt-in，不进入常规单元测试前提。

## 任务清单

- [ ] Step 1: 梳理当前 fake/offline agent loop 与 direct model client 的边界。
- [ ] Step 2: 定义真实模型 tool-call continuation 的内部 DTO，不把 provider SDK 类型泄漏到 Core 之外。
- [ ] Step 3: 将 `ToolDefinition` 渲染为 direct backend 可消费的工具定义。
- [ ] Step 4: 实现模型响应中的 tool call 解析、参数校验和 tool registry dispatch。
- [ ] Step 5: 将 tool result、structured payload、errorCode 和 approvalStatus 回写给模型 continuation。
- [ ] Step 6: 将真实 continuation 事件映射到 text/NDJSON/session/trace。
- [ ] Step 7: 增加 fake model continuation 测试，覆盖多轮 tool call、tool error、invalid args。
- [ ] Step 8: 增加 opt-in real model smoke，缺 key/model 时跳过并输出清晰说明。
- [ ] Step 9: 更新 capability status 和 known limitations。
- [ ] Step 10: 运行 build/test 并创建 `39_week_review.md`。

## 验收标准

- fake model 与 direct backend 共享同一 tool-call contract。
- 真实模型路径可以请求至少一个只读工具并继续生成最终回答。
- tool error 不会中断 CLI 进程，模型可收到结构化失败结果。
- 无 API key/model 时错误脱敏且退出码稳定。
- 常规单元测试不依赖真实网络。

## 周回顾模板

```markdown
## 第 39 周回顾

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

第 40 周输入：
-
```

