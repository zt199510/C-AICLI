# 第 31 周 Session Resume 与管理命令 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将会话系统从 export/clear 扩展为可恢复、可查看、可管理的 CLI 会话能力。

## 来源

- 第 29 周计划：`docs_md/weekly/29_week_agent_run_loop_v1.plan.md`
- 第 30 周计划：`docs_md/weekly/30_week_approval_permission_profiles.plan.md`
- 当前 session transcript v1 实现。

## 本周范围

- 新增 `session list`、`session show`、`session rename`、`session delete`。
- `chat` 和 `exec` 支持 `--resume <session>`。
- transcript 记录 agent loop tool calls。
- 支持 `session export --format json|markdown`。
- 保持 `session clear` 向后兼容，文档中推荐使用 `delete`。

## 任务清单

- [ ] Step 1: 扩展 conversation store，支持 list、exists、rename、delete。
- [ ] Step 2: 增加 transcript summary：name、createdAtUtc、updatedAtUtc、turn count、tool call count。
- [ ] Step 3: 实现 `session list`。
- [ ] Step 4: 实现 `session show <name>`。
- [ ] Step 5: 实现 `session rename <old> <new>`。
- [ ] Step 6: 实现 `session delete <name>`，并让 `clear` 调用相同逻辑。
- [ ] Step 7: `chat --resume` 和 `exec --resume` 加载已有会话上下文。
- [ ] Step 8: `session export --format markdown` 输出可读会话记录。
- [ ] Step 9: 增加测试覆盖非法名称、路径越界、缺失 session、rename 冲突、markdown export。
- [ ] Step 10: 更新 docs、运行 build/test、创建 `31_week_review.md`。

## 验收标准

- `session list` 能列出本地会话摘要。
- `chat --resume demo` 和 `exec --resume demo` 能复用同一 transcript。
- markdown export 不包含 API key 或其他 secret。
- transcript 路径仍不能越界。

## 周回顾模板

```markdown
## 第 31 周回顾

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

第 32 周输入：
-
```
