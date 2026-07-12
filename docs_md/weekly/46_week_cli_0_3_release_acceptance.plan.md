# 第 46 周 CLI 0.3.0 发布验收 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 对 Week 39-45 的真实 agent 能力做最终验收，生成 0.3.0 发布记录和发布包。

## 来源

- Week 39-45 周计划与回顾。
- `docs_md/weekly/46_week_cli_0_3_real_agent_schedule.md`
- `docs_md/release/final_acceptance_0.2.0.md`
- `tools/Build-Release.ps1`
- `tools/Invoke-SmokeTests.ps1`

## 本周范围

- 回归 0.2.0 底座能力。
- 回归 Week 39-45 真实 agent 能力。
- 更新版本元数据到 `0.3.0`。
- 更新 release docs。
- 生成 deterministic 0.3.0 release package。
- 记录 final acceptance、zip size 和 SHA256。

## 任务清单

- [x] Step 1: 汇总 Week 39-45 review 状态，列出未完成项。
- [x] Step 2: 确认所有 Deferred 能力边界仍准确。
- [x] Step 3: 更新 version metadata 到 `0.3.0`。
- [x] Step 4: 更新 CHANGELOG。
- [x] Step 5: 更新 configuration、quickstart、security model、known limitations、capability status。
- [x] Step 6: 更新 final acceptance，加入真实 agent 开发闭环验收清单。
- [x] Step 7: 运行 `dotnet build src/CSharpAiCli.sln -c Release`。
- [x] Step 8: 运行 `dotnet test src/CSharpAiCli.sln -c Release --no-build`。
- [x] Step 9: 运行 `tools/Invoke-SmokeTests.ps1`。
- [x] Step 10: 运行 `tools/Build-Release.ps1` 两次并确认 zip SHA256 deterministic。
- [x] Step 11: 创建 `46_week_review.md` 和 `docs_md/release/final_acceptance_0.3.0.md`。

## 验收标准

- Release build/test/smoke 均通过。
- 0.3.0 release zip 存在并记录 size/SHA256。
- fake/offline real-agent semantic tests 覆盖核心闭环。
- opt-in real model smoke 文档清晰且不污染常规 CI。
- capability status 准确标记 Accepted、Solidified、Deferred。

## 周回顾模板

```markdown
## 第 46 周回顾

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

后续输入：
-
```
