# 运行时、日志与诊断说明

## Runtime

当前项目目标框架为 `net9.0`，本机 SDK 为 `9.0.308`。当前仓库没有 `global.json` SDK 锁定。

`doctor` 负责显示：

- target framework
- dotnet SDK
- dotnet runtime
- SDK lock 状态
- workspace 路径和状态
- 用户配置路径
- 工作区配置路径
- 日志目录
- API key 是否存在

## 配置来源

第 4 周仍沿用第 3 周的最小配置 schema：

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "sk-example"
}
```

配置优先级：

- API key：`OPENAI_API_KEY` > 工作区配置 `apiKey` > 用户配置 `apiKey` > missing
- model：工作区配置 `model` > 用户配置 `model` > `not configured`

报告和日志只打印 API key 的 `present` 或 `missing`，以及来源；不打印原始 key。

## 日志目录

当工作区状态是 `ready`：

```text
<workspace>/.caicli/logs
```

当工作区状态是 `missing` 或 `not directory`：

```text
<user profile>/.caicli/logs
```

命令日志按 UTC 日期写入：

```text
yyyy-MM-dd.log
```

每行日志记录：

- `timestampUtc`
- `command`
- `workspace`
- `workspaceStatus`
- `model`
- `modelSource`
- `apiKey`
- `apiKeySource`
- `warnings`

日志不记录 `SecretValue.Value`。

## Chat 边界

第 4 周只添加 `chat` 的 Phase 02 边界提示。该命令用于告诉用户模型客户端和流式渲染器属于第 5-6 周，不执行模型调用。

`chat` 命令可以使用 `--workspace <path>`，用于显示与记录当前工作区上下文。
