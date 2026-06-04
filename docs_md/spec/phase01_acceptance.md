# Phase 01 验收清单

状态：待第 4 周 smoke 验证后标记为 Accepted

## 范围

Phase 01 覆盖第 1-4 周：

- solution 和项目骨架
- CLI 根命令
- `doctor`
- `config get`
- `chat` Phase 02 边界提示
- 工作区检测
- `--workspace <path>` 覆盖
- 用户配置与工作区配置加载
- 配置优先级
- API key 脱敏
- 轻量命令日志
- 诊断报告和测试基础

## 验收命令

```powershell
dotnet build src/CSharpAiCli.sln
dotnet test src/CSharpAiCli.sln
dotnet run --project src/CSharpAiCli.Cli -- --help
dotnet run --project src/CSharpAiCli.Cli -- doctor --workspace .
dotnet run --project src/CSharpAiCli.Cli -- config get --workspace .
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace .
```

## 必须通过的行为

- `dotnet build src/CSharpAiCli.sln` 成功。
- `dotnet test src/CSharpAiCli.sln` 成功。
- help 输出包含 `doctor`、`config`、`chat`、`--workspace` 和 `--help`。
- `doctor --workspace .` 以 `0` 退出。
- `doctor` 输出 SDK、runtime、target framework、SDK lock、workspace、config path、log directory 和 API key 状态。
- `config get --workspace .` 以 `0` 退出。
- `config get` 输出 workspace、config path、log directory、model、model source、api key 状态和 api key source。
- `chat --workspace .` 以 `2` 退出，返回 Phase 02 边界提示，不调用模型。
- `doctor`、`config get`、`chat`、命令日志和对象字符串不打印原始 API key。
- 工作区缺失或不是目录时，诊断命令不崩溃。
- 配置文件缺失时不产生 warning。
- 无效配置文件会被忽略，并显示 config warning。
- 命令日志写入失败不会阻止 `doctor` 或 `config get` 输出。

## Phase 02 输入

Phase 02 可以依赖以下基础：

- `CliEnvironmentSnapshot`
- `WorkspaceContext`
- `EffectiveConfiguration`
- `SecretValue`
- `ConfigLoader`
- `CommandLogger`
- `doctor` 和 `config get` 诊断输出
- `chat` 命令名和 Phase 02 边界位置

Phase 02 的第一个实现目标是 model client 抽象和 OpenAI SDK Responses API runner。
