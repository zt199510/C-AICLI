# 第 44 周 复核 Gate 与任务报告 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为真实 agent 任务增加只读复核 gate 和最终任务报告，使每次改动都能被用户复核。

## 来源

- `docs_md/weekly/33_week_diff_review_status_models.plan.md`
- `docs_md/weekly/37_week_observability_logs_trace.plan.md`
- `docs_md/weekly/43_week_failure_feedback_retry.plan.md`

## 本周范围

- 只读 review gate。
- 最终 task summary。
- report payload 进入 session/trace。
- report 默认不写额外文件；完整 markdown report Deferred 到 0.3.1-0.3.2。

## 任务清单

- [ ] Step 1: 定义 `AgentTaskReport`，包含 prompt、plan、tools、changed files、commands、verification、risks、trace path。
- [ ] Step 2: 在最终输出中加入 changed files、commands run、verification result 和 remaining risks。
- [ ] Step 3: 增加只读 review gate，对最终 diff 做 summary，不允许写文件。
- [ ] Step 4: report 中所有 secret 只记录 presence/source，不记录值。
- [ ] Step 5: 将 report payload 写入 session transcript 和 trace。
- [ ] Step 6: 增加 JSON event 中的 `taskReport` 事件。
- [ ] Step 7: 增加测试覆盖 secret redaction、no-change report、failed report、review gate read-only。
- [ ] Step 8: 更新 quickstart、security model 和 capability status。
- [ ] Step 9: 运行 build/test 并创建 `44_week_review.md`。

## 验收标准

- 每次 agent run 都有最终 task report。
- report 能说明成功、失败或部分完成。
- review gate 只读，不会触发 patch 或 shell。
- report 与 trace/session 中的关键字段一致。

## 周回顾模板

```markdown
## 第 44 周回顾

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

第 45 周输入：
-
```

