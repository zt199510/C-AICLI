# 第 36 周 安全边界与 Shell/Patch 加固 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 强化 workspace、shell、patch、MCP 工具执行边界，减少 agent loop 带来的误操作风险。

## 来源

- 第 30 周计划：`docs_md/weekly/30_week_approval_permission_profiles.plan.md`
- 第 35 周计划：`docs_md/weekly/35_week_mcp_real_stdio_v1.plan.md`
- 安全文档：`docs_md/release/security_model.md`

## 本周范围

- shell allowlist/denylist 配置。
- patch 多文件预览策略。
- shell cwd、环境变量和输出截断策略加固。
- MCP server 启动命令风险提示。
- `doctor` 增加安全边界诊断。

## 任务清单

- [ ] Step 1: 增加 shell policy 配置：allowedCommands、deniedCommands、maxTimeoutMilliseconds。
- [ ] Step 2: 修改 dangerous command detector，输出 matched rule。
- [ ] Step 3: shell tool 输出 command risk summary。
- [ ] Step 4: patch tool 增加 dry-run preview event，为未来多文件 patch 做接口准备。
- [ ] Step 5: MCP stdio server 启动前复用 shell command risk 检查。
- [ ] Step 6: `doctor` 显示 shell policy、patch policy、MCP execution policy。
- [ ] Step 7: 增加测试覆盖 allowlist、denylist、timeout 上限、matched rule、MCP server blocked。
- [ ] Step 8: 更新 security model 和 known limitations。
- [ ] Step 9: 运行 build/test 并创建 `36_week_review.md`。

## 验收标准

- denylist 命令无法被 shell tool 执行。
- 超过配置上限的 timeout 会被拒绝或压到上限，并在输出中说明。
- dangerous command 返回可读 matched rule。
- MCP server 启动命令受同一安全策略约束。

## 周回顾模板

```markdown
## 第 36 周回顾

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

第 37 周输入：
-
```
