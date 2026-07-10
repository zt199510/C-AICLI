# 第 42 周 Patch 与验证命令工作流 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 patch、changed files、git diff、验证命令执行和验证结果回写串成真实开发工作流。

## 来源

- `docs_md/weekly/11_week_patch_dirty_approval.plan.md`
- `docs_md/weekly/12_week_shell_runner_approval.plan.md`
- `docs_md/weekly/36_week_security_sandbox_boundaries.plan.md`
- `docs_md/weekly/41_week_context_gathering_planning.plan.md`

## 本周范围

- agent-driven patch apply。
- changed files tracking。
- 验证命令选择、审批、执行和结果记录。
- 将验证结果回写给模型 continuation。
- 不实现完整 rollback；只提供复核信息和手动撤销提示。

## 任务清单

- [ ] Step 1: 定义 agent run 的 `ChangedFileSummary`。
- [ ] Step 2: 将 patch preview、approval、apply result 纳入 agent step event。
- [ ] Step 3: patch 后收集 git diff summary 和 changed files。
- [ ] Step 4: 定义验证命令选择策略：project instructions 优先，其次 workflow/config，最后不自动猜危险命令。
- [ ] Step 5: shell verification 复用 approval、timeout、denylist 和 risk summary。
- [ ] Step 6: 将 verification stdout/stderr/truncation/status 回写给模型。
- [ ] Step 7: 增加测试覆盖 patch accepted/denied、file changed after preview、verification success/failure。
- [ ] Step 8: 更新 security model 和 quickstart。
- [ ] Step 9: 运行 build/test 并创建 `42_week_review.md`。

## 验收标准

- agent 可以申请 patch 并在审批后应用。
- changed files 可进入 text/JSON/session/trace。
- 验证命令执行结果可被模型继续消费。
- patch 拒绝、shell 拒绝和 timeout 都是可追踪失败。

## 周回顾模板

```markdown
## 第 42 周回顾

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

第 43 周输入：
-
```

