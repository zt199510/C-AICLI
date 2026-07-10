# 第 41 周 上下文收集与有限计划 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让真实 agent 在修改代码前先收集必要上下文，并形成可追踪、有限范围的执行计划。

## 来源

- `docs_md/weekly/32_week_project_instructions_agents_md.plan.md`
- `docs_md/weekly/33_week_diff_review_status_models.plan.md`
- `docs_md/weekly/40_week_agent_state_machine_limits.plan.md`

## 本周范围

- 统一任务启动上下文：workspace、git status、project instructions、session resume。
- 建立 bounded context gathering 策略。
- 增加只读 planning step，输出简短 plan 并进入 transcript/trace。
- 不实现完整 `@file`/`@folder` 引用；该能力 Deferred 到 0.3.1-0.3.2。

## 任务清单

- [ ] Step 1: 定义 `AgentTaskContext`，包含 cwd、workspace、instructions、session、git summary。
- [ ] Step 2: 将 `AGENTS.md`/`AICLI.md` 加载结果纳入 agent run 上下文事件。
- [ ] Step 3: 增加初始 git status/diff summary 的 bounded 收集策略。
- [ ] Step 4: 为 read/search 工具调用增加 planning 阶段约束，不读取 workspace 外路径。
- [ ] Step 5: 实现 `plan` step 事件，记录目标、候选文件、预期工具和风险。
- [ ] Step 6: 增加 plan truncation warning，避免上下文过大。
- [ ] Step 7: 增加测试覆盖 instructions source、dirty workspace、large diff、workspace boundary。
- [ ] Step 8: 更新 quickstart 和 capability status。
- [ ] Step 9: 运行 build/test 并创建 `41_week_review.md`。

## 验收标准

- `exec` 在写文件前能产生可追踪 plan。
- 上下文收集不会越过 workspace guard。
- dirty workspace、large diff 和 missing git 均有清晰事件。
- plan 过长时有 truncation warning。

## 周回顾模板

```markdown
## 第 41 周回顾

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

第 42 周输入：
-
```

