# 第 45 周 真实 Agent Smoke 与文档加固 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 扩展 smoke tests 和 release docs，覆盖 0.3.0 真实 agent 关键路径，同时保持常规 CI 不依赖真实模型。

## 来源

- `tools/Invoke-SmokeTests.ps1`
- `docs_md/release/capability_status.md`
- `docs_md/release/known_limitations.md`
- `docs_md/weekly/44_week_review_gate_task_report.plan.md`

## 本周范围

- 离线 fake model end-to-end smoke。
- opt-in real model smoke。
- approval/security regression smoke。
- 0.3.0 使用文档。
- Deferred 边界更新。

## 任务清单

- [ ] Step 1: 扩展 smoke fixtures，增加小型 bugfix 仓库或测试项目。
- [ ] Step 2: 增加 fake model end-to-end smoke：read/search/patch/verify/report。
- [ ] Step 3: 增加 approval denied、dangerous shell、workspace boundary、tool disabled regression smoke。
- [ ] Step 4: 增加 opt-in real model smoke 开关，例如环境变量或显式参数。
- [ ] Step 5: 真实模型 smoke 缺凭据时必须 skip，不得 fail 常规 smoke。
- [ ] Step 6: 更新 quickstart：如何运行真实 agent、如何查看 trace、如何复核 diff。
- [ ] Step 7: 更新 security model、known limitations、capability status。
- [ ] Step 8: 更新 troubleshooting：常见模型/tool/approval/verification 失败。
- [ ] Step 9: 运行 build/test/smoke 并创建 `45_week_review.md`。

## 验收标准

- 默认 smoke 不访问网络。
- opt-in real model smoke 路径文档清晰。
- 真实 agent 关键路径有离线 smoke 覆盖。
- docs 不把已实现能力继续标为 Deferred。

## 周回顾模板

```markdown
## 第 45 周回顾

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

第 46 周输入：
-
```

