# 第 34 周 工具系统增强与错误码统一 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 统一工具 schema、事件、错误码和 CLI 调用体验，为 agent loop 和 MCP 扩展打稳基础。

## 来源

- 第 29 周计划：`docs_md/weekly/29_week_agent_run_loop_v1.plan.md`
- 第 30 周计划：`docs_md/weekly/30_week_approval_permission_profiles.plan.md`
- 第 33 周计划：`docs_md/weekly/33_week_diff_review_status_models.plan.md`

## 本周范围

- `tools list --json`。
- `tools call` 支持 stdin JSON。
- 工具错误码统一 registry。
- tool result 增加 structured data。
- 所有工具调用输出都可映射为 JSON event。
- 增加工具文档生成片段。

## 任务清单

- [ ] Step 1: 定义 `ToolErrorCode` 常量或 registry。
- [ ] Step 2: 为 `ToolExecutionResult` 增加 optional structured payload。
- [ ] Step 3: 修改 read/search/git/shell/patch 工具，返回统一错误码和 payload。
- [ ] Step 4: 实现 `tools list --json`。
- [ ] Step 5: 实现 `tools call <name> --stdin` 读取 stdin JSON。
- [ ] Step 6: 增加 tool schema renderer，用于 docs 和模型工具定义。
- [ ] Step 7: 增加测试覆盖 JSON schema、stdin、错误码稳定性、disabled tool。
- [ ] Step 8: 更新 capability status、quickstart 和 tool 文档。
- [ ] Step 9: 运行 build/test 并创建 `34_week_review.md`。

## 验收标准

- `tools list --json` 输出稳定、可解析。
- `tools call --stdin` 可从管道读取 JSON 参数。
- 常见失败均有稳定 `errorCode`。
- agent loop 可以直接消费 structured tool result。

## 周回顾模板

```markdown
## 第 34 周回顾

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

第 35 周输入：
-
```
