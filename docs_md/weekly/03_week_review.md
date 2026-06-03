# 第 03 周回顾
状态：已稳固

已完成：
- 添加 `WorkspaceContext` 工作区检测器。
- 添加 `--workspace <path>` 覆盖支持。
- 添加用户配置和工作区配置加载。
- 添加配置优先级：`OPENAI_API_KEY` > 工作区配置 > 用户配置 > 默认值。
- 添加 `SecretValue` 脱敏包装，报告和对象字符串不打印原始密钥。
- 更新 `doctor` 和 `config get` 报告，显示工作区状态、配置来源和密钥来源。

验证：
- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：Build succeeded，0 warnings，0 errors
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：Failed: 0，Passed: 26，Skipped: 0
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- --help`
- 结果：输出包含 `doctor`、`config`、`--workspace` 和 `--help`
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- doctor --workspace .`
- 结果：输出包含 `C# AI CLI doctor`、工作区路径、`workspace status: ready`、用户配置路径、工作区配置路径和 `api key: missing`
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- config get --workspace .`
- 结果：在临时设置 `OPENAI_API_KEY` 时输出 `apiKey: present` 和 `apiKeySource: OPENAI_API_KEY`，并且不打印密钥值
- 命令：临时工作区配置优先级 smoke
- 结果：输出 `model: gpt-week3-workspace`、`modelSource: workspace config`、`apiKey: present`、`apiKeySource: workspace config` 和已加载的工作区配置路径，并且不打印文件中的密钥值

运行时说明：
- 当前目标框架仍为 `net9.0`。
- 本地 .NET SDK 仍为 `9.0.308`。
- 当前没有 `global.json` SDK 锁定。

风险：
- 配置 schema 仍是最小 schema，只覆盖 `model` 和 `apiKey`。
- API key 已可由配置加载器读取，但本阶段仍没有模型调用。
- 工作区检测只负责诊断和路径解析，尚未实现文件访问越界保护。

第 04 周输入：
- 稳定基础能力、日志、诊断和 Phase 01 文档。
- 补齐 `chat` 占位命令或明确推迟到 Phase 02。
- 记录 Phase 01 验收清单。
