## 第 27 周回顾

状态：已验收

已完成：
- 在配置模型中加入 `baseUrl` / `baseUrlSource`，包含校验规则和官方 OpenAI 默认端点。
- 实现优先级：`OPENAI_BASE_URL` > user config `baseUrl` > workspace config `baseUrl` > default。
- OpenAI Responses SDK gateway 已使用有效 base URL。
- 新增 `caicli config list`。
- 新增 `caicli config set <key> <value>`，支持标量 user config keys（`model`、`baseUrl`、`agentBackend`、`apiKey`），包含校验和脱敏输出。
- 新增 `caicli config unset <key>`，用于移除 user config keys。
- 更新 `doctor`、`config get/list`、命令日志和发布文档，展示 base URL/source 与 API key presence/source，且不泄露 secret。
- 新增/更新测试，覆盖优先级、无效 URL、secret 不泄露和 config 命令输出。
- 使用 Subagent-Driven execution，并对每一步进行 spec review 和 quality review。

验证：
- 命令：`dotnet build src/CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。
- 命令：`dotnet test src/CSharpAiCli.sln -c Release`
- 结果：通过，307 tests，0 failed，0 skipped。
- Step 7 目标命令：`dotnet test src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj -c Release --filter "ConfigLoaderTests|DoctorReportTests|ConfigReportTests|CommandLoggerTests"`
- 结果：通过，48/48。
- Step 8 目标命令：`dotnet test src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj -c Release --filter "ConfigLoaderTests|CliCommandFactoryTests|ConfigReportTests|CommandLoggerTests|ConfigFileEditorTests"`
- 结果：通过，94/94。

运行时说明：
- 未设置配置时，默认 base URL 仍为官方 OpenAI endpoint。
- 允许 workspace `baseUrl`，但会通过 source reporting 在诊断信息中可见。
- `config get/list`、edit-result output 和 command logs 只报告 secret 状态/source，不输出原始 key 值。
- 无效 URL/backend warnings 避免回显被拒绝的原始值。

风险：
- 自定义 base URL 依赖目标 gateway 对 OpenAI Responses API 的兼容性。
- `agentBackend=framework` 仍受当前 experimental/stub backend 状态限制。
- `apiKey` 仍可通过命令写入 user config，因此文档建议在共享或敏感环境中优先使用环境变量。

第 28 周输入：
- 考虑为自定义 base URL 配置增加 release smoke 覆盖。
- 考虑在 doctor 和 command logger 之间集中 backend diagnostic status formatting。
- 考虑未来是否需要为 workspace `baseUrl` 增加额外 trust policy controls。
