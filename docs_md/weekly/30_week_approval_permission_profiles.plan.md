# 第 30 周 审批与权限 Profile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立更接近 Codex CLI 的审批策略和权限 profile，使读、写、shell、危险命令都有明确边界。

## 来源

- 第 29 周计划：`docs_md/weekly/29_week_agent_run_loop_v1.plan.md`
- 安全文档：`docs_md/release/security_model.md`
- 当前 shell/patch 工具测试。

## 本周范围

- 新增 `approvalMode` 配置和 `--approval` 命令行覆盖。
- 支持 `never`、`on-request`、`on-failure`、`always`。
- 定义 tool risk level：read、write、shell、dangerous-shell。
- `doctor` 展示有效审批模式。
- `exec` 和 `tools call` 使用同一套审批决策。

## 任务清单

- [ ] Step 1: 增加 `ApprovalMode` 枚举和配置加载。
- [ ] Step 2: 为工具定义增加 risk level metadata。
- [ ] Step 3: 实现 approval policy resolver。
- [ ] Step 4: `tools call` 支持 `--approval <mode>`，保留 `--approve` 兼容。
- [ ] Step 5: `exec` 支持 `--approval <mode>`。
- [ ] Step 6: 修改 shell 和 patch 工具，使 approval request 包含 risk level、command/path 和 reason。
- [ ] Step 7: 增加测试覆盖四种 approval mode、dangerous command、兼容 `--approve`。
- [ ] Step 8: 更新 security model、configuration 和 quickstart。
- [ ] Step 9: 运行 build/test 并创建 `30_week_review.md`。

## 验收标准

- 默认策略不允许静默写文件或执行 shell。
- `--approval always` 可以通过本地 smoke 写文件和安全 shell。
- dangerous shell 即使请求审批也必须给出明确风险状态。
- 所有审批结果在 text 和 JSON event 中可见。

## 周回顾模板

```markdown
## 第 30 周回顾

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

第 31 周输入：
-
```
