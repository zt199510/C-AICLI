# 第 32 周 项目指令与 AGENTS.md 兼容 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 增强项目指令加载能力，兼容 `AGENTS.md`，并让 agent/chat/exec 都能使用稳定的上下文规则。

## 来源

- 第 31 周计划：`docs_md/weekly/31_week_session_resume_management.plan.md`
- 当前 `AICLI.md` 指令加载能力。
- Codex-like CLI 差距分析。

## 本周范围

- 支持 `AGENTS.md`。
- 保留 `AICLI.md` 向后兼容。
- 实现从 workspace root 到目标路径的层级指令加载。
- `doctor` 显示加载的 instruction 文件列表。
- `exec` 支持 `--cwd` 或 task target path 时加载对应路径层级指令。

## 任务清单

- [ ] Step 1: 定义 instruction file discovery 规则：`AGENTS.md` 优先，`AICLI.md` 兼容。
- [ ] Step 2: 实现层级 discovery，保证路径必须在 workspace 内。
- [ ] Step 3: 合并指令时记录 source path 和顺序。
- [ ] Step 4: 修改 `CliEnvironmentSnapshot`，携带 instruction sources。
- [ ] Step 5: `doctor` 和 `config get/list` 显示 instruction sources。
- [ ] Step 6: `chat` 和 `exec` 将合并后的 instructions 传给模型。
- [ ] Step 7: 增加测试覆盖 root/subdir、多文件合并、越界路径、空文件。
- [ ] Step 8: 更新 quickstart、安全文档和 capability status。
- [ ] Step 9: 运行 build/test 并创建 `32_week_review.md`。

## 验收标准

- workspace 根目录 `AGENTS.md` 可影响 `chat` 和 `exec`。
- 子目录 `AGENTS.md` 可追加更具体规则。
- `AICLI.md` 旧项目仍可用。
- 指令文件路径和内容不会从 workspace 外加载。

## 周回顾模板

```markdown
## 第 32 周回顾

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

第 33 周输入：
-
```
