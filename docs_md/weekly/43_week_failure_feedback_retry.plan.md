# 第 43 周 失败反馈与有限重试 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将测试失败、shell failure 和 tool error 反馈给模型，支持有限重试，并在预算耗尽时输出可复核失败报告。

## 来源

- `docs_md/weekly/40_week_agent_state_machine_limits.plan.md`
- `docs_md/weekly/42_week_patch_verify_workflow.plan.md`
- `docs_md/spec/product_positioning_and_roadmap.md`

## 本周范围

- 失败分类：model/tool/approval/patch/shell/verification/budget。
- verification failure feedback。
- retry budget。
- retry 后 changed files 和 command history 合并。
- 失败报告，不做无限尝试。

## 任务清单

- [ ] Step 1: 定义 `AgentFailureKind` 和 retry decision 输入。
- [ ] Step 2: 将 verification failure 摘要为模型可消费的 bounded feedback。
- [ ] Step 3: 增加 retry budget 配置，默认值保守。
- [ ] Step 4: 实现一次失败后再次 read/search/patch/verify 的 fake end-to-end 测试。
- [ ] Step 5: 超过 retry budget 时生成 failure summary。
- [ ] Step 6: 确保 retry 不绕过 approval、workspace guard 或 dirty checks。
- [ ] Step 7: 记录每次 retry 的 commands、changed files、stop reason。
- [ ] Step 8: 更新 capability status 和 known limitations。
- [ ] Step 9: 运行 build/test 并创建 `43_week_review.md`。

## 验收标准

- 一次 verification failure 可以反馈给模型进行有限修复。
- retry budget 耗尽时任务明确失败，并输出剩余风险。
- retry 不会静默追加高风险操作。
- fake end-to-end 测试覆盖失败后修复路径。

## 周回顾模板

```markdown
## 第 43 周回顾

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

第 44 周输入：
-
```

