# 第 38 周 CLI 0.2.0 发布验收 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 对 Week 27-37 的 CLI 完善能力做最终验收，生成 0.2.0 发布记录和发布包。

## 来源

- Week 27-37 周计划与回顾。
- `docs_md/release/final_acceptance.md`
- `tools/Build-Release.ps1`
- `tools/Invoke-SmokeTests.ps1`

## 本周范围

- 回归所有 Week 27-37 新能力。
- 扩展 smoke tests 覆盖 config、exec、agent loop、approval、session、instructions、MCP stdio、logs。
- 更新 release docs。
- 生成 deterministic 0.2.0 release package。
- 更新 capability status 和 known limitations。

## 任务清单

- [ ] Step 1: 汇总 Week 27-37 review 状态，列出未完成项。
- [ ] Step 2: 更新 smoke tests，覆盖 0.2.0 CLI 新命令。
- [ ] Step 3: 更新 version metadata 到 `0.2.0`。
- [ ] Step 4: 更新 CHANGELOG。
- [ ] Step 5: 更新 configuration、quickstart、security model、known limitations、capability status。
- [ ] Step 6: 运行 `dotnet build src/CSharpAiCli.sln -c Release`。
- [ ] Step 7: 运行 `dotnet test src/CSharpAiCli.sln -c Release --no-build`。
- [ ] Step 8: 运行 `tools/Build-Release.ps1` 两次并确认 zip SHA256 deterministic。
- [ ] Step 9: 运行 `tools/Invoke-SmokeTests.ps1`。
- [ ] Step 10: 创建 `38_week_review.md` 和 `docs_md/release/final_acceptance_0.2.0.md`。

## 验收标准

- 所有 Release build/test/smoke 均通过。
- 0.2.0 release zip 存在并记录 size/SHA256。
- capability status 准确标记 Accepted、Solidified、Deferred。
- 文档不再把已实现能力列为 Deferred。

## 周回顾模板

```markdown
## 第 38 周回顾

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
