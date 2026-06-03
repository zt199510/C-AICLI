# 第 02 周回顾

状态：已稳固

已完成：
- 添加了 `System.CommandLine` 根命令支持。
- 添加了 `doctor` 命令。
- 添加了 `config get` 命令。
- 添加了运行时环境快照、doctor 报告和配置报告测试。
- API 密钥处理仅保留为存在/缺失状态。

验证：
- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：构建成功，0 个警告，0 个错误。
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：失败：0，通过：9，跳过：0。
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- --help`
- 结果：输出包含 `doctor`、`config`、`--help` 和 `--version`。
- 命令：在 `OPENAI_API_KEY` 为空时运行 `dotnet run --project src/CSharpAiCli.Cli -- doctor`。
- 结果：命令以 0 退出，输出包含 `api key: missing`。
- 命令：使用非空测试 API 密钥运行 `dotnet run --project src/CSharpAiCli.Cli -- config get`。
- 结果：命令以 0 退出，输出包含 `apiKey: present`，并且不会打印密钥值。

运行时说明：
- 当前目标框架仍为 `net9.0`。
- 本地 .NET SDK 为 `9.0.308`。
- 当前没有 `global.json` SDK 锁定，因此 `doctor` 报告 `sdk lock: not locked`。

风险：
- 项目仍使用本地 SDK 选择，而不是提交到仓库的 `global.json`。
- `config get` 目前会打印计划中的最小有效配置摘要，尚未读取真实配置文件。
- 工作区默认使用当前目录；尚未实现 `--workspace <path>` 覆盖支持。

第 03 周输入：
- 添加工作区检测。
- 添加配置加载。
- 添加配置优先级和密钥脱敏测试。
- 添加 `--workspace <path>` 覆盖支持。
