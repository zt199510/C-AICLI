# 第 28 周 非交互 Exec 与 JSON 事件流 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增适合脚本和 CI 使用的 `caicli exec` 命令，并建立稳定的机器可读事件输出。

## 来源

- 当前能力状态：`docs_md/release/capability_status.md`
- 第 27 周计划：`docs_md/weekly/27_week_config_base_url.plan.md`
- release quickstart：`docs_md/release/quickstart.md`

## 本周范围

- 新增 `caicli exec "<task>"`。
- 支持 `--json` 输出 newline-delimited JSON events。
- 支持 `--output text|json`，默认 text。
- 复用当前 direct backend、tool registry、approval policy 和 workspace guard。
- 先实现非交互任务执行协议，不在本周实现完整模型工具循环。

## 任务清单

- [ ] Step 1: 定义 exec request/result/event 的 core DTO。
- [ ] Step 2: 实现 text event renderer 和 JSON event renderer。
- [ ] Step 3: 新增 `caicli exec "<task>"` 命令入口。
- [ ] Step 4: 将现有 `run` smoke task 能力迁移为 exec runner 的 deterministic fallback。
- [ ] Step 5: 保持 `run` 兼容，将 `run` 标记为 smoke/direct-task alias 或内部调用 exec runner。
- [ ] Step 6: 支持 exit code：成功为 0，任务失败为 1，参数错误为 2。
- [ ] Step 7: 增加测试覆盖 text 输出、JSON 输出、失败事件、审批拒绝和 workspace 越界。
- [ ] Step 8: 更新 quickstart、capability status 和 known limitations。
- [ ] Step 9: 运行 `dotnet build src/CSharpAiCli.sln -c Release` 和 `dotnet test src/CSharpAiCli.sln -c Release`。
- [ ] Step 10: 创建 `28_week_review.md`。

## 验收标准

- `caicli exec "read README.md"` 可以执行只读工作区任务。
- `caicli exec --json "read README.md"` 输出合法 NDJSON。
- `caicli run` 旧命令仍可通过 smoke tests。
- 所有 exec 事件不泄露 API key。

## 周回顾模板

```markdown
## 第 28 周回顾

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

第 29 周输入：
-
```
