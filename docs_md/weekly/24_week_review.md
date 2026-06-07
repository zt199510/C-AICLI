# 第 24 周回顾

状态：已稳固

已完成：
- 添加安装文档：`docs_md/release/installation.md`。
- 添加配置文档：`docs_md/release/configuration.md`。
- 添加安全模型文档：`docs_md/release/security_model.md`。
- 添加快速开始：`docs_md/release/quickstart.md`。
- 添加能力状态矩阵：`docs_md/release/capability_status.md`。
- 明确 direct 后端为 MVP；Microsoft Agent Framework 真实后端、真实 MCP handshake/discovery、Gerber/TIFF 真实执行和 dotnet tool 包为 Deferred 或非首版硬门槛。

验证：
- 命令：`artifacts\release\caicli-0.1.0-win-x64\caicli.exe version`
- 结果：通过，输出 `caicli 0.1.0`、`target framework: net9.0`、`release runtime: win-x64`。
- 命令：`artifacts\release\caicli-0.1.0-win-x64\caicli.exe doctor --workspace <clean-temp-workspace>`
- 结果：通过，报告 `api key: missing` 和 `agent backend status: available`。
- 命令：`artifacts\release\caicli-0.1.0-win-x64\caicli.exe chat --workspace <clean-temp-workspace> "hello"`，无 model/key。
- 结果：按预期非零退出，报告 `localErrorCode: missing-model`。
- 命令：设置 `OPENAI_MODEL=gpt-test` 且无 key 后运行同一 chat。
- 结果：按预期非零退出，报告 `localErrorCode: missing-openai-api-key`。
- 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，237 tests passed。

运行时说明：
- 文档中的用户路径按当前实现记录：user config 为 `%USERPROFILE%\.caicli\config.json`，session 为 `%USERPROFILE%\.caicli\sessions`，workspace logs 为 `<workspace>\.caicli\logs`。
- `chat` validation 顺序为先 model 后 key；quickstart 已按实际行为描述。

风险：
- 文档已覆盖 `run`、tools、MCP 和安全边界所需概念，但 `run`、session export/clear 和工具禁用 CLI 表面命令还需要 Week 25 加固。

第 25 周输入：
- 添加 `tools/Invoke-SmokeTests.ps1`。
- 加固 `run`、工具禁用、审批拒绝、路径越界和 shell timeout 的发布 smoke 路径。
