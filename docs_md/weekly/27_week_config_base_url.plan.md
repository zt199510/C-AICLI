# 第 27 周 配置增强与 Base URL 支持 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完善配置系统，支持 OpenAI base URL、自助配置命令和更清晰的配置诊断。

## 来源

- 当前能力状态：`docs_md/release/capability_status.md`
- 配置文档：`docs_md/release/configuration.md`
- 已知限制：`docs_md/release/known_limitations.md`
- 第 26 周验收：`docs_md/weekly/26_week_review.md`

## 本周范围

- 支持 `OPENAI_BASE_URL` 环境变量。
- 支持 user config `baseUrl`。
- 明确 workspace config `baseUrl` 策略：允许工作区声明非敏感 endpoint，但必须在 `doctor` 和 `config get` 中展示来源；如后续安全策略决定禁用，可在本周实现为 warning。
- 增加 `caicli config set/get/list/unset` 的最小可用命令。
- `doctor` 和 `config get` 显示 base URL、model、backend、key 状态和配置来源。
- OpenAI SDK gateway 使用有效 base URL。

## 任务清单

- [ ] Step 1: 在配置模型中加入 `BaseUrl`、`BaseUrlSource` 和验证规则。
- [ ] Step 2: 扩展配置加载优先级：`OPENAI_BASE_URL` > user config `baseUrl` > workspace config `baseUrl` > OpenAI 默认 endpoint。
- [ ] Step 3: 修改 OpenAI Responses gateway，使其根据有效配置创建 SDK client endpoint。
- [ ] Step 4: 增加 `caicli config list`，打印所有非 secret 配置项和来源。
- [ ] Step 5: 增加 `caicli config set <key> <value>`，默认写入 user config。
- [ ] Step 6: 增加 `caicli config unset <key>`，默认从 user config 删除配置项。
- [ ] Step 7: 更新 `doctor`、`config get`、日志脱敏和 release 配置文档。
- [ ] Step 8: 增加单元测试覆盖 env/user/workspace/default 优先级、非法 URL、secret 不泄露和命令输出。
- [ ] Step 9: 运行 `dotnet build src/CSharpAiCli.sln -c Release` 和 `dotnet test src/CSharpAiCli.sln -c Release`。
- [ ] Step 10: 创建 `27_week_review.md`，记录验证命令和遗留风险。

## 验收标准

- `OPENAI_BASE_URL` 可覆盖默认 OpenAI endpoint。
- user config `baseUrl` 可被 `chat` 使用。
- `config get/list` 不打印 API key 值。
- `doctor` 能清楚显示 base URL 来源。
- 无 base URL 配置时仍使用官方 OpenAI 默认 endpoint。

## 周回顾模板

```markdown
## 第 27 周回顾

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

第 28 周输入：
-
```
