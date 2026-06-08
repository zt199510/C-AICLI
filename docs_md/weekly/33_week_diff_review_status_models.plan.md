# 第 33 周 Diff、Review、Status 与 Models 命令 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 增加高频开发命令，使 CLI 更像日常可用的代码助手入口。

## 来源

- 第 32 周计划：`docs_md/weekly/32_week_project_instructions_agents_md.plan.md`
- 当前 git status/diff 工具。
- release quickstart 和 capability status。

## 本周范围

- 新增 `caicli status`。
- 新增 `caicli diff`。
- 新增 `caicli review`，默认只读审查，不改文件。
- 新增 `caicli models`，显示当前模型配置与常用模型提示，不调用外网模型列表 API。
- `review` 支持 text 和 JSON 输出。

## 任务清单

- [ ] Step 1: 实现 `status` 命令，汇总 workspace、git status、配置状态、approval mode。
- [ ] Step 2: 实现 `diff` 命令，复用 git diff tool 并支持 `--stat`。
- [ ] Step 3: 实现 `review` 命令，读取 git diff 并构造审查 prompt。
- [ ] Step 4: `review` 输出 findings-first 格式，失败时不改文件。
- [ ] Step 5: 实现 `models` 命令，展示当前 model/baseUrl/source 和推荐配置示例。
- [ ] Step 6: 增加测试覆盖无 git 仓库、有 git 仓库、空 diff、review 模型失败。
- [ ] Step 7: 更新 docs 和 smoke tests。
- [ ] Step 8: 运行 build/test 并创建 `33_week_review.md`。

## 验收标准

- `caicli status` 可在非 git workspace 下运行。
- `caicli diff` 在 git workspace 中输出当前 diff。
- `caicli review` 默认不执行 patch/shell。
- `caicli models` 不需要 API key。

## 周回顾模板

```markdown
## 第 33 周回顾

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

第 34 周输入：
-
```
